using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Events;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Handlers;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Handlers
{
    public class AddMemoryCommandHandlerTests
    {
        private readonly Mock<IEventStore> _eventStoreMock;
        private readonly Mock<IEventBus> _eventBusMock;
        private readonly Mock<ITextChunker> _chunkerMock;
        private readonly Mock<IOptions<AiProviderOptions>> _optionsMock;

        public AddMemoryCommandHandlerTests()
        {
            _eventStoreMock = new Mock<IEventStore>();
            _eventBusMock = new Mock<IEventBus>();
            _chunkerMock = new Mock<ITextChunker>();
            _optionsMock = new Mock<IOptions<AiProviderOptions>>();

            _optionsMock.Setup(o => o.Value).Returns(new AiProviderOptions
            {
                Chunking = new ChunkingOptions { Enabled = false, MaxTokens = 500, OverlapTokens = 50 }
            });
        }

        [Fact]
        public async Task Handle_Should_Append_And_Publish_Events_Successfully()
        {
            // Arrange
            var handler = new AddMemoryCommandHandler(
                _eventStoreMock.Object,
                _eventBusMock.Object,
                _chunkerMock.Object,
                _optionsMock.Object
            );

            var command = new AddMemoryCommand(
                RawText: "Test raw text payload",
                Source: MemorySource.User.ToString(),
                Importance: MemoryImportance.High,
                Tags: new List<string> { "test" },
                UserId: "user-123",
                CorrelationId: "corr-789"
            );

            // Act
            var resultId = await handler.Handle(command, CancellationToken.None);

            // Assert
            resultId.Should().NotBeEmpty();
            _eventStoreMock.Verify(s => s.AppendEventsAsync(
                It.Is<string>(st => st.StartsWith("memory-")),
                It.Is<IReadOnlyList<MemoryEvent>>(evs => evs.Count == 1),
                0,
                It.IsAny<CancellationToken>()
            ), Times.Once);

            _eventBusMock.Verify(b => b.PublishAsync(
                It.Is<IEnumerable<MemoryEvent>>(evs => System.Linq.Enumerable.Any(evs)),
                It.IsAny<CancellationToken>()
            ), Times.Once);
        }

        [Fact]
        public async Task Handle_Should_Map_Custom_Source_String_To_System_With_Tag()
        {
            // Arrange
            var handler = new AddMemoryCommandHandler(
                _eventStoreMock.Object,
                _eventBusMock.Object,
                _chunkerMock.Object,
                _optionsMock.Object
            );

            var command = new AddMemoryCommand(
                RawText: "Authentication failed",
                Source: "AuthenticationService",
                Importance: MemoryImportance.Medium,
                Tags: new List<string> { "security" },
                UserId: "user-123",
                CorrelationId: "corr-101"
            );

            // Act
            var resultId = await handler.Handle(command, CancellationToken.None);

            // Assert
            resultId.Should().NotBeEmpty();
            _eventStoreMock.Verify(s => s.AppendEventsAsync(
                It.Is<string>(st => st.StartsWith("memory-")),
                It.Is<IReadOnlyList<MemoryEvent>>(evs => 
                    evs.Count == 1 && 
                    ((MemoryAddedEvent)evs[0]).Source == MemorySource.System.ToString() &&
                    ((MemoryAddedEvent)evs[0]).Tags.Contains("source:AuthenticationService")),
                0,
                It.IsAny<CancellationToken>()
            ), Times.Once);
        }
    }
}
