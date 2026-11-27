using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Business.Workers;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.Context;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;
using PersonalAIAssistant.Memory.Infrastructure.Sql;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure;

public sealed class RetentionWorkerIntegrationTests
{
    [Fact]
    public async Task Sweep_ArchivesExpiredMemory_ThroughEventStoreAndReadModelRepository()
    {
        await using var fixture = new RetentionFixture(hardDeleteEnabled: false, ttlDays: 1, archiveDays: 30);
        var memoryId = await fixture.SeedMemoryAsync(createdAt: DateTime.UtcNow.AddDays(-2));
        (await fixture.GetExpiredCandidatesAsync(1)).Should().ContainSingle(c => c.MemoryId == memoryId);

        await fixture.Worker.RunRetentionSweepAsync(CancellationToken.None);

        var history = await fixture.EventStore.GetEventsAsync($"memory-{memoryId}", CancellationToken.None);
        history.Should().ContainSingle(e => e is PersonalAIAssistant.Memory.Events.MemoryArchivedEvent);
        history.Should().NotContain(e => e is PersonalAIAssistant.Memory.Events.MemoryDeletedEvent);
    }

    [Fact]
    public async Task Sweep_HardDeletesOldArchivedMemory_ButPreservesLegalHold()
    {
        await using var fixture = new RetentionFixture(hardDeleteEnabled: true, ttlDays: 365, archiveDays: 0);
        var archivedId = await fixture.SeedArchivedMemoryAsync(legalHold: false);
        var heldId = await fixture.SeedArchivedMemoryAsync(legalHold: true);
        var candidates = (await fixture.GetArchivedCandidatesAsync(0)).Select(c => c.MemoryId).ToList();
        candidates.Should().Contain(archivedId).And.Contain(heldId);

        await fixture.Worker.RunRetentionSweepAsync(CancellationToken.None);

        var archivedHistory = await fixture.EventStore.GetEventsAsync($"memory-{archivedId}", CancellationToken.None);
        archivedHistory.Should().ContainSingle(e => e is PersonalAIAssistant.Memory.Events.MemoryDeletedEvent);

        var heldHistory = await fixture.EventStore.GetEventsAsync($"memory-{heldId}", CancellationToken.None);
        heldHistory.Should().NotContain(e => e is PersonalAIAssistant.Memory.Events.MemoryDeletedEvent);
    }

    private sealed class RetentionFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        public InMemoryEventStore EventStore { get; } = new();
        public RetentionWorker Worker { get; }

        public RetentionFixture(bool hardDeleteEnabled, int ttlDays, int archiveDays)
        {
            var collection = new ServiceCollection();
            collection.AddLogging();
            var databaseName = $"retention-{Guid.NewGuid():N}";
            collection.AddDbContext<ReadModelDbContext>(options =>
                options.UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            collection.AddSingleton<IEncryptionService>(Mock.Of<IEncryptionService>());
            collection.Configure<EncryptionOptions>(options => options.Enabled = false);
            collection.AddSingleton<IEventStore>(EventStore);
            collection.AddScoped<SqlReadModelRepository>();
            collection.AddScoped<IReadModelRepository>(sp => sp.GetRequiredService<SqlReadModelRepository>());
            collection.AddScoped<IRetentionQueryStore>(sp => sp.GetRequiredService<SqlReadModelRepository>());
            collection.AddScoped<IProcessingLockStore>(sp => sp.GetRequiredService<SqlReadModelRepository>());

            _services = collection.BuildServiceProvider();
            Worker = new RetentionWorker(
                NullLogger<RetentionWorker>.Instance,
                _services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new RetentionOptions
                {
                    HardDeleteEnabled = hardDeleteEnabled,
                    TtlDays = ttlDays,
                    ArchiveDays = archiveDays,
                    MaxMemoriesPerUser = int.MaxValue
                }));
        }

        public async Task<Guid> SeedMemoryAsync(DateTime createdAt)
        {
            var aggregate = new MemoryAggregate();
            aggregate.AddMemory("retention integration test memory", MemorySource.User, userId: "retention-user");
            var memoryId = aggregate.Id.Value;
            var streamId = $"memory-{memoryId}";
            var events = aggregate.UncommittedEvents.ToList();
            await EventStore.AppendEventsAsync(streamId, events, 0, CancellationToken.None);
            await UpsertReadModelAsync(memoryId, createdAt, archived: false);
            return memoryId;
        }

        public async Task<Guid> SeedArchivedMemoryAsync(bool legalHold)
        {
            var aggregate = new MemoryAggregate();
            aggregate.AddMemory("retention integration test memory", MemorySource.User, userId: "retention-user");
            aggregate.Archive("test setup", "system");
            if (legalHold) aggregate.ApplyLegalHold("preserve during test", "auditor");

            var memoryId = aggregate.Id.Value;
            await EventStore.AppendEventsAsync($"memory-{memoryId}", aggregate.UncommittedEvents.ToList(), 0, CancellationToken.None);
            await UpsertReadModelAsync(memoryId, DateTime.UtcNow.AddDays(-5), archived: true);
            return memoryId;
        }

        public async Task<IReadOnlyList<ReadModelCandidate>> GetExpiredCandidatesAsync(int ttlDays)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReadModelDbContext>();
            var cutoff = DateTime.UtcNow.AddDays(-ttlDays);
            (await db.MemoryReadModels.AsNoTracking().CountAsync(m => !m.Archived && m.CreatedAt < cutoff))
                .Should().BeGreaterThan(0, "the real read model should contain an expired row");
            var query = scope.ServiceProvider.GetRequiredService<IRetentionQueryStore>();
            return (await query.GetExpiredMemoriesAsync(ttlDays, CancellationToken.None)).ToList();
        }

        public async Task<IReadOnlyList<ReadModelCandidate>> GetArchivedCandidatesAsync(int olderThanDays)
        {
            using var scope = _services.CreateScope();
            var query = scope.ServiceProvider.GetRequiredService<IRetentionQueryStore>();
            return (await query.GetArchivedMemoriesAsync(olderThanDays, CancellationToken.None)).ToList();
        }

        private async Task UpsertReadModelAsync(Guid memoryId, DateTime createdAt, bool archived)
        {
            using var scope = _services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IReadModelRepository>();
            await repo.UpsertAsync(new MemoryReadModel
            {
                MemoryId = memoryId,
                UserId = "retention-user",
                Summary = "retention integration test memory",
                CreatedAt = createdAt,
                Archived = archived,
                Importance = MemoryImportance.Medium,
                TokenCount = 8
            }, CancellationToken.None);
            var saved = (await repo.GetMemoriesByIdsAsync(new[] { memoryId }, CancellationToken.None)).ToList();
            saved.Should().ContainSingle(m => m.CreatedAt == createdAt && m.Archived == archived);
        }

        public ValueTask DisposeAsync()
        {
            _services.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
