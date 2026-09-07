using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Security;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Models;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class AuthorizationBehaviorTests
    {
        private readonly Mock<IReadModelRepository> _readRepoMock = new();

        private AuthorizationBehavior<TRequest, Guid> CreateBehavior<TRequest>() where TRequest : notnull
        {
            return new AuthorizationBehavior<TRequest, Guid>(
                _readRepoMock.Object,
                NullLogger<AuthorizationBehavior<TRequest, Guid>>.Instance);
        }

        [Fact]
        public async Task Handle_CompressMemoryCommand_SameUser_Proceeds()
        {
            var memoryId = Guid.NewGuid();
            var memory = new MemoryReadModel
            {
                MemoryId = memoryId,
                UserId = "user-1",
                TenantId = "default"
            };

            _readRepoMock.Setup(r => r.GetMemoriesByIdsAsync(
                It.Is<IEnumerable<Guid>>(ids => ids.Contains(memoryId)),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MemoryReadModel> { memory });

            var behavior = CreateBehavior<CompressMemoryCommand>();
            var command = new CompressMemoryCommand(memoryId, "Summary", "gpt-4o-mini", 50, "user-1");

            var nextCalled = false;
            RequestHandlerDelegate<Guid> next = (ct) =>
            {
                nextCalled = true;
                return Task.FromResult(memoryId);
            };

            var result = await behavior.Handle(command, next, CancellationToken.None);

            nextCalled.Should().BeTrue();
            result.Should().Be(memoryId);
        }

        [Fact]
        public async Task Handle_CompressMemoryCommand_DifferentUser_ThrowsUnauthorizedAccessException()
        {
            var memoryId = Guid.NewGuid();
            var memory = new MemoryReadModel
            {
                MemoryId = memoryId,
                UserId = "victim-user",
                TenantId = "default"
            };

            _readRepoMock.Setup(r => r.GetMemoriesByIdsAsync(
                It.Is<IEnumerable<Guid>>(ids => ids.Contains(memoryId)),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MemoryReadModel> { memory });

            var behavior = CreateBehavior<CompressMemoryCommand>();
            var command = new CompressMemoryCommand(memoryId, "Malicious overwrite", "gpt-4o-mini", 50, "attacker-user");

            RequestHandlerDelegate<Guid> next = (ct) => Task.FromResult(memoryId);

            var act = () => behavior.Handle(command, next, CancellationToken.None);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("*not authorized*");
        }

        [Fact]
        public async Task Handle_UpdateMemoryCommand_CrossTenant_ThrowsUnauthorizedAccessException()
        {
            var memoryId = Guid.NewGuid();
            var memory = new MemoryReadModel
            {
                MemoryId = memoryId,
                UserId = "user-1",
                TenantId = "tenant-a"
            };

            _readRepoMock.Setup(r => r.GetMemoriesByIdsAsync(
                It.Is<IEnumerable<Guid>>(ids => ids.Contains(memoryId)),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MemoryReadModel> { memory });

            var behavior = CreateBehavior<UpdateMemoryCommand>();
            var command = new UpdateMemoryCommand(memoryId, "user-1", new Dictionary<string, string> { { "RawText", "new" } }, TenantId: "tenant-b");

            RequestHandlerDelegate<Guid> next = (ct) => Task.FromResult(memoryId);

            var act = () => behavior.Handle(command, next, CancellationToken.None);

            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("*Cross-tenant access denied*");
        }
    }
}
