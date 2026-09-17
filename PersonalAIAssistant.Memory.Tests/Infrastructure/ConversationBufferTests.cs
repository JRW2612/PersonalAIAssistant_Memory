using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.InMemory;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure;

public class ConversationBufferTests
{
    private readonly InMemoryConversationBuffer _buffer;
    private readonly ConversationBufferOptions _options;

    public ConversationBufferTests()
    {
        _options = new ConversationBufferOptions
        {
            MaxTurns = 5,
            FlushThreshold = 3,
            SessionTtlMinutes = 30,
            AutoFlushToMemory = true
        };

        _buffer = new InMemoryConversationBuffer(
            Options.Create(_options),
            NullLogger<InMemoryConversationBuffer>.Instance);
    }

    [Fact]
    public async Task AppendTurnAsync_WithinMaxTurns_PreservesAllTurns()
    {
        var userId = "user1";
        var sessionId = "sess1";

        await _buffer.AppendTurnAsync(userId, sessionId, "user", "Hello assistant");
        await _buffer.AppendTurnAsync(userId, sessionId, "assistant", "Hello! How can I help?");

        var turns = await _buffer.GetRecentTurnsAsync(userId, sessionId, 10);
        var count = await _buffer.GetTurnCountAsync(userId, sessionId);

        count.Should().Be(2);
        turns.Should().HaveCount(2);
        turns[0].Role.Should().Be("user");
        turns[0].Content.Should().Be("Hello assistant");
        turns[1].Role.Should().Be("assistant");
        turns[1].Content.Should().Be("Hello! How can I help?");
    }

    [Fact]
    public async Task AppendTurnAsync_ExceedsMaxTurns_TrimsOldest()
    {
        var userId = "user1";
        var sessionId = "sess1";

        // MaxTurns is 5; append 7
        for (int i = 1; i <= 7; i++)
        {
            await _buffer.AppendTurnAsync(userId, sessionId, "user", $"Message {i}");
        }

        var turns = await _buffer.GetRecentTurnsAsync(userId, sessionId, 10);
        var count = await _buffer.GetTurnCountAsync(userId, sessionId);

        count.Should().Be(5);
        turns.Should().HaveCount(5);
        turns[0].Content.Should().Be("Message 3");
        turns[4].Content.Should().Be("Message 7");
    }

    [Fact]
    public async Task FlushAndClearAsync_ReturnsTurns_AndClearsBuffer()
    {
        var userId = "user2";
        var sessionId = "sess2";

        await _buffer.AppendTurnAsync(userId, sessionId, "user", "Fact 1");
        await _buffer.AppendTurnAsync(userId, sessionId, "assistant", "Fact 2");

        var flushed = await _buffer.FlushAndClearAsync(userId, sessionId);
        var remainingCount = await _buffer.GetTurnCountAsync(userId, sessionId);

        flushed.Should().HaveCount(2);
        remainingCount.Should().Be(0);
    }

    [Fact]
    public async Task ClearAsync_RemovesBufferSession()
    {
        var userId = "user3";
        var sessionId = "sess3";

        await _buffer.AppendTurnAsync(userId, sessionId, "user", "Temporary note");
        await _buffer.ClearAsync(userId, sessionId);

        var turns = await _buffer.GetRecentTurnsAsync(userId, sessionId, 5);
        turns.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleSessions_AreIsolated()
    {
        var userId = "user1";
        var sessionA = "desktop";
        var sessionB = "mobile";

        await _buffer.AppendTurnAsync(userId, sessionA, "user", "Desktop message");
        await _buffer.AppendTurnAsync(userId, sessionB, "user", "Mobile message");

        var turnsA = await _buffer.GetRecentTurnsAsync(userId, sessionA, 5);
        var turnsB = await _buffer.GetRecentTurnsAsync(userId, sessionB, 5);

        turnsA.Should().ContainSingle(t => t.Content == "Desktop message");
        turnsB.Should().ContainSingle(t => t.Content == "Mobile message");
    }
}
