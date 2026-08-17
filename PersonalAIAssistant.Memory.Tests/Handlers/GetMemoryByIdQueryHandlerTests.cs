using FluentAssertions;
using Moq;
using PersonalAIAssistant.Memory.Business.Handlers;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Handlers
{
    public class GetMemoryByIdQueryHandlerTests
    {
        private readonly Mock<IReadModelRepository> _readRepoMock;
        private readonly GetMemoryByIdQueryHandler _handler;

        public GetMemoryByIdQueryHandlerTests()
        {
            _readRepoMock = new Mock<IReadModelRepository>();
            _handler = new GetMemoryByIdQueryHandler(_readRepoMock.Object);
        }

        [Fact]
        public async Task Handle_MemoryExistsAndUserMatches_ReturnsMemoryReadModel()
        {
            // Arrange
            var memoryId = Guid.NewGuid();
            var model = new MemoryReadModel
            {
                MemoryId = memoryId,
                UserId = "user-123",
                Summary = "Important architectural note",
                Importance = MemoryImportance.High,
                CreatedAt = DateTime.UtcNow
            };

            _readRepoMock
                .Setup(r => r.GetMemoriesByIdsAsync(It.Is<Guid[]>(ids => ids.Length == 1 && ids[0] == memoryId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MemoryReadModel> { model });

            var query = new GetMemoryByIdQuery(memoryId, "user-123");

            // Act
            var result = await _handler.Handle(query, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result!.MemoryId.Should().Be(memoryId);
            result.Summary.Should().Be("Important architectural note");
        }

        [Fact]
        public async Task Handle_MemoryNotFound_ReturnsNull()
        {
            // Arrange
            var memoryId = Guid.NewGuid();
            _readRepoMock
                .Setup(r => r.GetMemoriesByIdsAsync(It.IsAny<Guid[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MemoryReadModel>());

            var query = new GetMemoryByIdQuery(memoryId, "user-123");

            // Act
            var result = await _handler.Handle(query, CancellationToken.None);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task Handle_UserMismatch_ReturnsNull()
        {
            // Arrange
            var memoryId = Guid.NewGuid();
            var model = new MemoryReadModel
            {
                MemoryId = memoryId,
                UserId = "different-user",
                Summary = "Confidential note"
            };

            _readRepoMock
                .Setup(r => r.GetMemoriesByIdsAsync(It.IsAny<Guid[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MemoryReadModel> { model });

            var query = new GetMemoryByIdQuery(memoryId, "user-123");

            // Act
            var result = await _handler.Handle(query, CancellationToken.None);

            // Assert
            result.Should().BeNull();
        }
    }
}
