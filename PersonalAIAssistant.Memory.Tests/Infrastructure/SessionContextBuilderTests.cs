using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Business.Services;
using PersonalAIAssistant.Memory.Infrastructure.InMemory;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure;

public class SessionContextBuilderTests
{
    private readonly InMemoryConversationBuffer _buffer;
    private readonly Mock<IMemoryRetrievalService> _mockRetrieval;
    private readonly Mock<ICompressionService> _mockCompression;
    private readonly Mock<IMediator> _mockMediator;
    private readonly ConversationBufferOptions _options;
    private readonly SessionContextBuilder _builder;

    public SessionContextBuilderTests()
    {
        _options = new ConversationBufferOptions
        {
            MaxTurns = 10,
            FlushThreshold = 3,
            AutoFlushToMemory = true
        };

        _buffer = new InMemoryConversationBuffer(
            Options.Create(_options),
            NullLogger<InMemoryConversationBuffer>.Instance);

        _mockRetrieval = new Mock<IMemoryRetrievalService>();
        _mockCompression = new Mock<ICompressionService>();
        _mockMediator = new Mock<IMediator>();
        var mockFactRepo = new Mock<PersonalAIAssistant.Memory.Core.Interfaces.Persistence.ITemporalFactRepository>();
        mockFactRepo.Setup(r => r.GetCurrentFactsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TemporalFact>());

        _builder = new SessionContextBuilder(
            _buffer,
            _mockRetrieval.Object,
            _mockCompression.Object,
            _mockMediator.Object,
            mockFactRepo.Object,
            Options.Create(_options),
            NullLogger<SessionContextBuilder>.Instance);
    }

    [Fact]
    public async Task BuildAsync_AssemblesRecentTurnsAndMemories_IntoFormattedPrompt()
    {
        var userId = "user1";
        var sessionId = "sessionA";

        await _buffer.AppendTurnAsync(userId, sessionId, "user", "What is my project deadline?");
        await _buffer.AppendTurnAsync(userId, sessionId, "assistant", "Checking your calendar...");

        var retrieved = new List<RetrievedMemory>
        {
            new(
                Guid.NewGuid(),
                "User project Alpha deadline is set to October 15th.",
                Score: 0.92,
                VectorScore: 0.90,
                RecencyScore: 0.95,
                Importance: MemoryImportance.High,
                CreatedAt: DateTime.UtcNow.AddDays(-2))
        };

        _mockRetrieval
            .Setup(r => r.RetrieveAsync(It.IsAny<RetrievalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(retrieved);

        var request = new SessionContextRequest(
            UserId: userId,
            SessionId: sessionId,
            CurrentMessage: "What is my deadline again?",
            TopK: 3,
            MaxBufferTurns: 5);

        var result = await _builder.BuildAsync(request);

        result.Should().NotBeNull();
        result.RecentTurns.Should().HaveCount(2);
        result.LongTermMemories.Should().HaveCount(1);
        result.SystemPromptFragment.Should().Contain("Relevant Long-Term Memories");
        result.SystemPromptFragment.Should().Contain("User project Alpha deadline is set to October 15th.");
        result.SystemPromptFragment.Should().Contain("Recent Conversation History");
        result.SystemPromptFragment.Should().Contain("What is my project deadline?");
    }

    [Fact]
    public async Task BuildAsync_WhenBufferExceedsFlushThreshold_TriggersCompressionAndPromotion()
    {
        var userId = "user2";
        var sessionId = "sessionB";

        // Flush threshold is 3; append 3 turns
        await _buffer.AppendTurnAsync(userId, sessionId, "user", "I prefer dark mode in UI.");
        await _buffer.AppendTurnAsync(userId, sessionId, "assistant", "Noted.");
        await _buffer.AppendTurnAsync(userId, sessionId, "user", "Also always use C# 12.");

        _mockRetrieval
            .Setup(r => r.RetrieveAsync(It.IsAny<RetrievalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RetrievedMemory>());

        _mockCompression
            .Setup(c => c.CompressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompressionResult("User prefers dark mode and C# 12.", "test-model", 8));

        _mockMediator
            .Setup(m => m.Send(It.IsAny<AddMemoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var request = new SessionContextRequest(
            UserId: userId,
            SessionId: sessionId,
            CurrentMessage: "What UI style do I like?");

        var result = await _builder.BuildAsync(request);

        // Verification: compression was called with turns transcript
        _mockCompression.Verify(c => c.CompressAsync(It.Is<string>(s => s.Contains("dark mode")), It.IsAny<CancellationToken>()), Times.Once);

        // Verification: AddMemoryCommand was sent via mediator
        _mockMediator.Verify(m => m.Send(It.Is<AddMemoryCommand>(cmd =>
            cmd.UserId == userId &&
            cmd.Source == "ConversationBuffer" &&
            cmd.RawText == "User prefers dark mode and C# 12."), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuildAsync_WhenRetrievalFails_StillReturnsTurnsGracefully()
    {
        var userId = "user3";
        var sessionId = "sessionC";

        await _buffer.AppendTurnAsync(userId, sessionId, "user", "Hello assistant");

        _mockRetrieval
            .Setup(r => r.RetrieveAsync(It.IsAny<RetrievalRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Vector store unavailable"));

        var request = new SessionContextRequest(
            UserId: userId,
            SessionId: sessionId,
            CurrentMessage: "Hello again");

        var result = await _builder.BuildAsync(request);

        result.Should().NotBeNull();
        result.RecentTurns.Should().HaveCount(1);
        result.LongTermMemories.Should().BeEmpty();
        result.SystemPromptFragment.Should().Contain("No relevant long-term memories found");
    }
}
