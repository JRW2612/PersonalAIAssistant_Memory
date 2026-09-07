using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Handlers;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Models;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Handlers
{
    public class ConsolidateMemoriesCommandHandlerTests
    {
        private readonly Mock<IEventStore> _eventStoreMock = new();
        private readonly Mock<IEventBus> _eventBusMock = new();
        private readonly Mock<IAIProviderFactory> _aiFactoryMock = new();
        private readonly Mock<IReadModelRepository> _readRepoMock = new();

        [Fact]
        public async Task Handle_MemoryBelongsToDifferentUser_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var mem1 = Guid.NewGuid();
            var mem2 = Guid.NewGuid();

            var models = new List<MemoryReadModel>
            {
                new() { MemoryId = mem1, UserId = "caller-user" },
                new() { MemoryId = mem2, UserId = "victim-user" } // Different owner!
            };

            _readRepoMock.Setup(r => r.GetMemoriesByIdsAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(models);

            var handler = new ConsolidateMemoriesCommandHandler(
                _eventStoreMock.Object,
                _eventBusMock.Object,
                _aiFactoryMock.Object,
                _readRepoMock.Object,
                NullLogger<ConsolidateMemoriesCommandHandler>.Instance);

            var command = new ConsolidateMemoriesCommand(
                NewMemoryId: Guid.NewGuid(),
                MergedMemoryIds: new List<Guid> { mem1, mem2 },
                ConsolidatedText: "Merged summary",
                UserId: "caller-user",
                ProvenanceLinks: new List<string>()
            );

            // Act
            var act = () => handler.Handle(command, CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("*do not belong to the current user*");
        }
    }
}
