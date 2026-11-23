using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Business.Projectors;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Events;
using PersonalAIAssistant.Memory.Infrastructure.Context;
using PersonalAIAssistant.Memory.Infrastructure.Messaging;
using PersonalAIAssistant.Memory.Infrastructure.Sql;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure;

public class OrderedEventProjectionTests
{
    [Fact]
    public void Partitioning_IsStableForAnAggregate()
    {
        var aggregateId = Guid.NewGuid();

        MemoryEventPartitioning.GetPartition(aggregateId).Should()
            .Be(MemoryEventPartitioning.GetPartition(aggregateId));
        MemoryEventPartitioning.GetPartition(aggregateId).Should()
            .BeInRange(0, MemoryEventPartitioning.PartitionCount - 1);
        MemoryEventPartitioning.GetQueueName(aggregateId).Should()
            .StartWith("memory-event-partition-");
    }

    [Fact]
    public async Task Projector_RejectsVersionGapWithoutChangingProjectionOrProcessedMarker()
    {
        using var db = CreateDbContext();
        var repo = CreateRepository(db);
        var memoryId = Guid.NewGuid();
        await repo.UpsertAsync(new MemoryReadModel
        {
            MemoryId = memoryId,
            Summary = "version one",
            Version = 1,
            CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);
        var projector = CreateProjector(repo);

        var evt = new MemoryUpdatedEvent
        {
            AggregateId = memoryId,
            MemoryId = memoryId,
            Version = 3,
            UpdatedFields = new Dictionary<string, string> { ["RawText"] = "version three" }
        };

        var act = () => projector.HandleAsync(evt, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*aggregate {memoryId} version 3; expected version 2*");
        (await repo.GetMemoriesByIdsAsync(new[] { memoryId }, CancellationToken.None))
            .Single().Summary.Should().Be("version one");
        (await repo.HasProcessedAsync(memoryId, 3, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Projector_DiscardsStaleEventAndMarksItProcessedAtomically()
    {
        using var db = CreateDbContext();
        var repo = CreateRepository(db);
        var memoryId = Guid.NewGuid();
        await repo.UpsertAsync(new MemoryReadModel
        {
            MemoryId = memoryId,
            Summary = "newer summary",
            Version = 2,
            CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);
        var projector = CreateProjector(repo);

        await projector.HandleAsync(new MemoryCompressedEvent
        {
            AggregateId = memoryId,
            OriginalMemoryId = memoryId,
            Version = 1,
            CompressedText = "stale summary"
        }, CancellationToken.None);

        var result = (await repo.GetMemoriesByIdsAsync(new[] { memoryId }, CancellationToken.None)).Single();
        result.Summary.Should().Be("newer summary");
        result.Version.Should().Be(2);
        (await repo.HasProcessedAsync(memoryId, 1, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task EmbeddingClaim_IsDurableAndRejectsDuplicateAttempts()
    {
        using var db = CreateDbContext();
        var repo = CreateRepository(db);
        var aggregateId = Guid.NewGuid();

        (await repo.TryStartAsync(aggregateId, 1, CancellationToken.None)).Should().BeTrue();
        (await repo.TryStartAsync(aggregateId, 1, CancellationToken.None)).Should().BeFalse();
        (await db.EmbeddingIndexingRecords.CountAsync()).Should().Be(1);
    }

    private static ReadModelDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ReadModelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ReadModelDbContext(options);
    }

    private static SqlReadModelRepository CreateRepository(ReadModelDbContext db)
        => new(
            db,
            Mock.Of<IEncryptionService>(),
            Options.Create(new EncryptionOptions { Enabled = false }));

    private static MemoryEventProjector CreateProjector(SqlReadModelRepository repo)
        => new(repo, repo, Mock.Of<ITemporalFactRepository>());
}
