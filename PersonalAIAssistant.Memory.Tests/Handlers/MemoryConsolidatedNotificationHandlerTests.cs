using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalAIAssistant.Memory.Business.EventHandlers;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Events;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Handlers
{
    public class MemoryConsolidatedNotificationHandlerTests
    {
        private readonly Mock<INotificationSender> _notifierMock;
        private readonly Mock<ILogger<MemoryConsolidatedNotificationHandler>> _loggerMock;
        private readonly MemoryConsolidatedNotificationHandler _handler;

        public MemoryConsolidatedNotificationHandlerTests()
        {
            _notifierMock = new Mock<INotificationSender>();
            _loggerMock = new Mock<ILogger<MemoryConsolidatedNotificationHandler>>();
            _handler = new MemoryConsolidatedNotificationHandler(_notifierMock.Object, _loggerMock.Object);
        }

        [Fact]
        public async Task HandleAsync_ValidEvent_DispatchesTeamsNotification()
        {
            // Arrange
            var memoryId = Guid.NewGuid();
            var evt = new MemoryConsolidatedEvent
            {
                AggregateId = memoryId,
                NewMemoryId = memoryId,
                MergedMemoryIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
                ConsolidatedText = "Consolidated summary of meetings",
                UserId = "user-123"
            };

            // Act
            await _handler.HandleAsync(evt, CancellationToken.None);

            // Assert
            _notifierMock.Verify(n => n.SendAsync(
                "Memory Consolidated",
                It.Is<string>(b => b.Contains("Merged 2 memories") && b.Contains("Consolidated summary of meetings")),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task HandleAsync_NotifierThrows_DoesNotThrowException()
        {
            // Arrange
            var evt = new MemoryConsolidatedEvent
            {
                AggregateId = Guid.NewGuid(),
                ConsolidatedText = "Text",
                UserId = "user-123"
            };

            _notifierMock
                .Setup(n => n.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Teams server unavailable"));

            // Act
            Func<Task> act = async () => await _handler.HandleAsync(evt, CancellationToken.None);

            // Assert
            await act.Should().NotThrowAsync();
        }
    }
}
