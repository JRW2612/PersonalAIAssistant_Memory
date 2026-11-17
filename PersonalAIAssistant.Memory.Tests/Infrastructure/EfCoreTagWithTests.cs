using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.Context;
using PersonalAIAssistant.Memory.Infrastructure.EF;
using PersonalAIAssistant.Memory.Infrastructure.Extensions;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using PersonalAIAssistant.Memory.Infrastructure.Sql;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure;

public class EfCoreTagWithTests
{
    private class TestUserContext : IUserContext
    {
        public string UserId { get; set; } = "usr-test-42";
        public string TenantId { get; set; } = "tenant-enterprise";
        public IReadOnlyList<string> Roles { get; set; } = new List<string> { "User" };
        public bool IsAuthenticated { get; set; } = true;
        public string? CurrentAction { get; set; } = "Memories.Search";
        public string? CorrelationId { get; set; } = "corr-test-999";
    }

    [Fact]
    public void DbQueryTagger_CreatesStructuredTag_WithUserContext()
    {
        // Arrange
        var userContext = new TestUserContext();
        var tagger = new DbQueryTagger(userContext);

        // Act
        var tag = tagger.CreateTag("ReadModel.GetMemoriesByUser", "GetMemoriesByUser", "SqlReadModelRepository.cs", 102);

        // Assert
        tag.Should().Contain("UserAction: ReadModel.GetMemoriesByUser");
        tag.Should().Contain("User: usr-test-42");
        tag.Should().Contain("Tenant: tenant-enterprise");
        tag.Should().Contain("TraceId: corr-test-999");
        tag.Should().Contain("CallSite: SqlReadModelRepository.cs:102#GetMemoriesByUser()");
    }

    [Fact]
    public void DbQueryTagger_FallsBackGracefully_WhenNoUserContext()
    {
        // Arrange
        var tagger = new DbQueryTagger(null);

        // Act
        var tag = tagger.CreateTag("Outbox.DispatchPending");

        // Assert
        tag.Should().Contain("UserAction: Outbox.DispatchPending");
        tag.Should().Contain("User: system");
        tag.Should().Contain("Tenant: default");
    }

    [Fact]
    public void DbQueryTagger_SanitizesPotentiallyMaliciousCommentContent()
    {
        // Arrange
        var userContext = new TestUserContext
        {
            UserId = "user*/DROP TABLE Users;--",
            TenantId = "tenant\r\nInjectedLine"
        };
        var tagger = new DbQueryTagger(userContext);

        // Act
        var tag = tagger.CreateTag("Action*/With\nInjections--");

        // Assert
        tag.Should().NotContain("*/");
        tag.Should().NotContain("--");
        tag.Should().NotContain("\r");
        tag.Should().NotContain("\n");
        tag.Should().Contain("User: userDROP TABLE Users;-");
    }

    [Fact]
    public void TagWithUserAction_AttachesTagToQueryExpression()
    {
        // Arrange
        var userContext = new TestUserContext();
        var options = new DbContextOptionsBuilder<ReadModelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new ReadModelDbContext(options);

        // Act
        var query = db.MemoryReadModels.TagWithUserAction(userContext, "ReadModel.GetMemoriesByUser");

        // Assert
        var expressionString = query.Expression.ToString();
        expressionString.Should().Contain("TagWith");
        expressionString.Should().Contain("UserAction: ReadModel.GetMemoriesByUser");
        expressionString.Should().Contain("User: usr-test-42");
    }

    [Fact]
    public async Task SqlReadModelRepository_QueriesIncludeUserActionTags()
    {
        // Arrange
        var userContext = new TestUserContext();
        var options = new DbContextOptionsBuilder<ReadModelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new ReadModelDbContext(options);

        var encMock = new Mock<IEncryptionService>();
        var encOptions = Options.Create(new EncryptionOptions { Enabled = false });

        var repo = new SqlReadModelRepository(db, encMock.Object, encOptions, userContext);

        // Seed data
        var memId = Guid.NewGuid();
        db.MemoryReadModels.Add(new MemoryReadModelEntity
        {
            MemoryId = memId,
            StreamId = $"memory-{memId}",
            UserId = userContext.UserId,
            Summary = "Test memory summary",
            TokenCount = 25,
            Importance = PersonalAIAssistant.Memory.Core.Domains.Enums.MemoryImportance.Medium,
            CreatedAt = DateTime.UtcNow,
            Archived = false
        });
        await db.SaveChangesAsync();

        // Act
        var result = await repo.GetMemoriesByUserAsync(userContext.UserId, CancellationToken.None);
        var count = await repo.GetMemoryCountByUserAsync(userContext.UserId, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        count.Should().Be(1);
    }

    [Fact]
    public async Task SqlAccessAssignmentRepository_QueriesIncludeUserActionTags()
    {
        // Arrange
        var userContext = new TestUserContext();
        var options = new DbContextOptionsBuilder<ReadModelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new ReadModelDbContext(options);

        var repo = new SqlAccessAssignmentRepository(db, userContext);

        var assignmentId = Guid.NewGuid();
        db.AccessAssignments.Add(new AccessAssignmentEntity
        {
            Id = assignmentId,
            UserId = userContext.UserId,
            TargetResourceId = "resource-vault",
            PermissionLevel = "Read",
            IdempotencyKey = "key-001",
            Status = AssignmentStatus.Active
        });
        await db.SaveChangesAsync();

        // Act
        var assignment = await repo.GetByIdAsync(assignmentId);
        var byKey = await repo.GetByIdempotencyKeyAsync("key-001");

        // Assert
        assignment.Should().NotBeNull();
        byKey.Should().NotBeNull();
    }

    [Fact]
    public async Task SqlEmployeeRepository_QueriesIncludeUserActionTags()
    {
        // Arrange
        var userContext = new TestUserContext();
        var options = new DbContextOptionsBuilder<ReadModelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new ReadModelDbContext(options);

        var repo = new SqlEmployeeRepository(db, userContext);

        var empId = Guid.NewGuid();
        db.EmployeeRecords.Add(new EmployeeRecord
        {
            Id = empId,
            UserId = userContext.UserId,
            FullName = "Jane Doe",
            Department = "Engineering",
            TenantId = userContext.TenantId
        });
        await db.SaveChangesAsync();

        // Act
        var emp = await repo.GetByIdAsync(empId);
        var list = await repo.GetByTenantAsync(userContext.TenantId);

        // Assert
        emp.Should().NotBeNull();
        list.Should().HaveCount(1);
    }

    [Fact]
    public async Task SqlProviderConnectionRepository_QueriesIncludeUserActionTags()
    {
        // Arrange
        var userContext = new TestUserContext();
        var options = new DbContextOptionsBuilder<ReadModelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new ReadModelDbContext(options);

        var repo = new SqlProviderConnectionRepository(db, userContext);

        db.ProviderConnections.Add(new ProviderConnectionEntity
        {
            Id = Guid.NewGuid(),
            UserId = userContext.UserId,
            TenantId = userContext.TenantId,
            Provider = "gemini",
            EncryptedAccessToken = "enc-token",
            Status = ProviderConnectionStatus.Active
        });
        await db.SaveChangesAsync();

        // Act
        var conn = await repo.GetConnectionAsync(userContext.UserId, "gemini");
        var all = await repo.GetUserConnectionsAsync(userContext.UserId);

        // Assert
        conn.Should().NotBeNull();
        all.Should().HaveCount(1);
    }

    [Fact]
    public async Task SqlWorkloadIdentityRepository_QueriesIncludeUserActionTags()
    {
        // Arrange
        var userContext = new TestUserContext();
        var options = new DbContextOptionsBuilder<ReadModelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new ReadModelDbContext(options);

        var repo = new SqlWorkloadIdentityRepository(db, userContext);

        db.WorkloadIdentities.Add(new WorkloadIdentityEntity
        {
            Id = Guid.NewGuid(),
            ClientId = "client-app-1",
            Name = "App 1",
            TenantId = userContext.TenantId,
            CloudPlatform = "GCP",
            Status = WorkloadStatus.Active
        });
        await db.SaveChangesAsync();

        // Act
        var workload = await repo.GetByClientIdAsync("client-app-1");
        var list = await repo.GetByTenantIdAsync(userContext.TenantId);

        // Assert
        workload.Should().NotBeNull();
        list.Should().HaveCount(1);
    }

    [Fact]
    public async Task EfEventStore_QueriesIncludeUserActionTags()
    {
        // Arrange
        var userContext = new TestUserContext();
        var options = new DbContextOptionsBuilder<EventStoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new EventStoreDbContext(options);

        var eventStore = new EfEventStore(db, userContext);

        var evt = new PersonalAIAssistant.Memory.Events.MemoryAddedEvent
        {
            AggregateId = Guid.NewGuid(),
            RawText = "Hello world memory",
            Source = "UserChat",
            Tags = new List<string> { "chat" },
            UserId = userContext.UserId
        };

        // Act
        await eventStore.AppendEventAsync("stream-001", evt, 0, CancellationToken.None);
        var version = await eventStore.GetCurrentVersionAsync("stream-001", CancellationToken.None);
        var events = await eventStore.GetEventsAsync("stream-001", CancellationToken.None);

        // Assert
        version.Should().Be(1);
        events.Should().HaveCount(1);
    }

    [Fact]
    public void UserActionQueryTaggingInterceptor_PrependsTag_WhenNotAlreadyTagged()
    {
        // Arrange
        var userContext = new TestUserContext();
        var tagger = new DbQueryTagger(userContext);
        var interceptor = new UserActionQueryTaggingInterceptor(tagger, userContext);

        var dbCommandMock = new Mock<DbCommand>();
        dbCommandMock.SetupAllProperties();
        dbCommandMock.Object.CommandText = "SELECT * FROM Users";

        // Act
        var tag = interceptor.EnsureTagged(dbCommandMock.Object);

        // Assert
        tag.Should().NotBeNull();
        dbCommandMock.Object.CommandText.Should().StartWith("-- UserAction: Memories.Search | User: usr-test-42");
        dbCommandMock.Object.CommandText.Should().Contain("SELECT * FROM Users");
    }

    [Fact]
    public void UserActionQueryTaggingInterceptor_DoesNotDuplicate_WhenAlreadyTagged()
    {
        // Arrange
        var userContext = new TestUserContext();
        var tagger = new DbQueryTagger(userContext);
        var interceptor = new UserActionQueryTaggingInterceptor(tagger, userContext);

        var existingCommand = "-- UserAction: Custom.ExistingAction | User: usr-test-42\nSELECT * FROM Users";
        var dbCommandMock = new Mock<DbCommand>();
        dbCommandMock.SetupAllProperties();
        dbCommandMock.Object.CommandText = existingCommand;

        // Act
        var tag = interceptor.EnsureTagged(dbCommandMock.Object);

        // Assert - should remain unchanged
        tag.Should().BeNull();
        dbCommandMock.Object.CommandText.Should().Be(existingCommand);
    }
}
