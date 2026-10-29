using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Infrastructure.Context;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security;

public class AccessAssignmentOutboxTests
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ReadModelDbContext _db;
    private readonly IAccessAssignmentRepository _repository;

    public AccessAssignmentOutboxTests()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<ReadModelDbContext>(options =>
            options.UseInMemoryDatabase(dbName)
                   .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));

        services.AddScoped<IAccessAssignmentRepository, SqlAccessAssignmentRepository>();
        _serviceProvider = services.BuildServiceProvider();

        _db = _serviceProvider.GetRequiredService<ReadModelDbContext>();
        _repository = _serviceProvider.GetRequiredService<IAccessAssignmentRepository>();
    }

    [Fact]
    public async Task CreateAssignmentWithOutbox_AtomicallyCommitsBothAssignmentAndOutboxMessage()
    {
        // Arrange
        var assignmentId = Guid.NewGuid();
        var assignment = new AccessAssignmentEntity
        {
            Id = assignmentId,
            UserId = "user-123",
            TargetResourceId = "resource-vault-1",
            PermissionLevel = "Read",
            Status = AssignmentStatus.Pending,
            IdempotencyKey = "grant:user-123:resource-vault-1:Read:20261025"
        };

        var payload = JsonSerializer.Serialize(new { assignment.Id, assignment.UserId });

        // Act: Atomic single-transaction save
        var success = await _repository.CreateAssignmentWithOutboxAsync(
            assignment, 
            "AccessAssignmentCreatedEvent", 
            payload);

        // Assert
        Assert.True(success);
        var savedAssignment = await _db.AccessAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId);
        var savedOutbox = await _db.OutboxMessages.FirstOrDefaultAsync(m => m.MessageId == assignmentId);

        Assert.NotNull(savedAssignment);
        Assert.Equal(AssignmentStatus.Pending, savedAssignment.Status);
        Assert.NotNull(savedOutbox);
        Assert.Equal("AccessAssignmentCreatedEvent", savedOutbox.MessageType);
    }

    [Fact]
    public async Task Consumer_ProcessAssignment_IdempotentlyHandlesDuplicateEvents()
    {
        // Arrange
        var externalClientMock = new Mock<IExternalAccessClient>();
        externalClientMock.Setup(x => x.GrantAccessAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalGrantResult("grant-999", true));

        var consumer = new AccessAssignmentConsumer(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            externalClientMock.Object,
            NullLogger<AccessAssignmentConsumer>.Instance);

        var assignment = new AccessAssignmentEntity
        {
            Id = Guid.NewGuid(),
            UserId = "user-456",
            TargetResourceId = "doc-789",
            PermissionLevel = "Write",
            Status = AssignmentStatus.Pending,
            IdempotencyKey = "key-idem-456"
        };

        await _repository.CreateAssignmentWithOutboxAsync(assignment, "Event", "{}");

        var payload = JsonSerializer.Serialize(new AccessAssignmentPayload(
            assignment.Id, assignment.UserId, assignment.TargetResourceId, assignment.PermissionLevel, assignment.IdempotencyKey));

        // Act: Process the first time
        await consumer.ProcessAssignmentAsync(payload);

        // Act: Deliver duplicate message
        await consumer.ProcessAssignmentAsync(payload);

        // Assert: External client was only invoked ONCE due to idempotency guard
        externalClientMock.Verify(x => x.GrantAccessAsync(
            "user-456", "doc-789", "Write", "key-idem-456", It.IsAny<CancellationToken>()), Times.Once);

        var updated = await _repository.GetByIdAsync(assignment.Id);
        Assert.NotNull(updated);
        Assert.Equal(AssignmentStatus.Active, updated.Status);
        Assert.Equal("grant-999", updated.ExternalReferenceId);
    }

    [Fact]
    public async Task Consumer_Timeout_SetsStatusToRequiresReconciliation()
    {
        // Arrange: Simulate upstream gateway timeout
        var externalClientMock = new Mock<IExternalAccessClient>();
        externalClientMock.Setup(x => x.GrantAccessAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Downstream timed out"));

        // When queried, state is still unknown/not active yet
        externalClientMock.Setup(x => x.QueryGrantByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalGrantResult?)null);

        var consumer = new AccessAssignmentConsumer(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            externalClientMock.Object,
            NullLogger<AccessAssignmentConsumer>.Instance);

        var assignment = new AccessAssignmentEntity
        {
            Id = Guid.NewGuid(),
            UserId = "user-timeout",
            TargetResourceId = "vault-timeout",
            PermissionLevel = "Admin",
            Status = AssignmentStatus.Pending,
            IdempotencyKey = "key-timeout"
        };

        await _repository.CreateAssignmentWithOutboxAsync(assignment, "Event", "{}");

        var payload = JsonSerializer.Serialize(new AccessAssignmentPayload(
            assignment.Id, assignment.UserId, assignment.TargetResourceId, assignment.PermissionLevel, assignment.IdempotencyKey));

        // Act
        await consumer.ProcessAssignmentAsync(payload);

        // Assert: System treats timeout as indeterminate -> RequiresReconciliation (NOT premature failure)
        var updated = await _repository.GetByIdAsync(assignment.Id);
        Assert.NotNull(updated);
        Assert.Equal(AssignmentStatus.RequiresReconciliation, updated.Status);
    }

    [Fact]
    public async Task ReconciliationWorker_HealsInDoubtRecordsToActive()
    {
        // Arrange
        var assignment = new AccessAssignmentEntity
        {
            Id = Guid.NewGuid(),
            UserId = "user-reconcile",
            TargetResourceId = "vault-reconcile",
            PermissionLevel = "Read",
            Status = AssignmentStatus.RequiresReconciliation,
            IdempotencyKey = "key-reconcile",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        await _repository.CreateAssignmentWithOutboxAsync(assignment, "Event", "{}");

        var externalClientMock = new Mock<IExternalAccessClient>();
        // Remote query confirms the grant succeeded externally
        externalClientMock.Setup(x => x.QueryGrantByIdempotencyKeyAsync("key-reconcile", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalGrantResult("healed-grant-id", true));

        var worker = new ReconciliationWorker(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            externalClientMock.Object,
            NullLogger<ReconciliationWorker>.Instance);

        // Act
        await worker.RunReconciliationSweepAsync();

        // Assert: Status healed to Active
        var healed = await _repository.GetByIdAsync(assignment.Id);
        Assert.NotNull(healed);
        Assert.Equal(AssignmentStatus.Active, healed.Status);
        Assert.Equal("healed-grant-id", healed.ExternalReferenceId);
    }
}
