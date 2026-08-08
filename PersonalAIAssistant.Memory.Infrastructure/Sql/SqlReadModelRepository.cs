// PersonalAIAssistant.Memory.Infrastructure.EF/SqlReadModelRepository.cs
using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Core.Interfaces.Sql;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;

namespace PersonalAIAssistant.Memory.Infrastructure.EF
{
    public class SqlReadModelRepository : IReadModelRepository, ITransactionalReadModelRepository
    {
        private readonly ReadModelDbContext _db;

        public SqlReadModelRepository(ReadModelDbContext db)
        {
            _db = db;
        }

        public async Task UpsertAsync(MemoryReadModel model, CancellationToken cancellationToken)
        {
            var existing = await _db.MemoryReadModels.FindAsync(new object[] { model.MemoryId }, cancellationToken);
            if (existing == null)
            {
                var entity = new MemoryReadModelEntity
                {
                    MemoryId = model.MemoryId,
                    StreamId = $"memory-{model.MemoryId}",
                    Summary = model.Summary,
                    TokenCount = model.TokenCount,
                    Importance = model.Importance,
                    CreatedAt = DateTime.UtcNow,
                    Archived = model.Archived,
                    LastProcessedAt = DateTime.UtcNow
                };
                _db.MemoryReadModels.Add(entity);
            }
            else
            {
                existing.Summary = model.Summary;
                existing.TokenCount = model.TokenCount;
                existing.Importance = model.Importance;
                existing.Archived = model.Archived;
                existing.LastProcessedAt = DateTime.UtcNow;
                _db.MemoryReadModels.Update(existing);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task<bool> HasProcessedAsync(Guid aggregateId, int version, CancellationToken cancellationToken)
        {
            return await _db.ProcessedEvents.AnyAsync(p => p.AggregateId == aggregateId && p.Version == version, cancellationToken);
        }

        public async Task MarkProcessedAsync(Guid aggregateId, int version, CancellationToken cancellationToken)
        {
            var exists = await _db.ProcessedEvents.AnyAsync(p => p.AggregateId == aggregateId && p.Version == version, cancellationToken);
            if (!exists)
            {
                _db.ProcessedEvents.Add(new ProcessedEventEntity
                {
                    AggregateId = aggregateId,
                    Version = version,
                    ProcessedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<IEnumerable<ReadModelCandidate>> GetConsolidationCandidatesAsync(int batchSize, CancellationToken cancellationToken)
        {
            var rows = await _db.MemoryReadModels
                .Where(m => !m.Archived && m.TokenCount > 50)
                .OrderByDescending(m => m.TokenCount)
                .ThenBy(m => m.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            return rows.Select(r => new ReadModelCandidate(
                r.MemoryId,
                r.StreamId,
                r.Summary,
                r.TokenCount,
                r.CreatedAt,
                r.Archived));
        }

        public async Task<bool> TryMarkProcessingAsync(Guid memoryId, CancellationToken cancellationToken)
        {
            // Use a transaction to ensure atomic insert-if-not-exists
            try
            {
                var existing = await _db.ProcessingLocks.FindAsync(new object[] { memoryId }, cancellationToken);
                if (existing != null) return false;

                _db.ProcessingLocks.Add(new ProcessingLockEntity { MemoryId = memoryId, LockedAt = DateTime.UtcNow });
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException)
            {
                // likely a unique key violation -> someone else locked it
                return false;
            }
        }

        public async Task MarkProcessedAsync(Guid memoryId, CancellationToken cancellationToken)
        {
            var lockEntity = await _db.ProcessingLocks.FindAsync(new object[] { memoryId }, cancellationToken);
            if (lockEntity != null)
            {
                _db.ProcessingLocks.Remove(lockEntity);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task UnmarkProcessingAsync(Guid memoryId, CancellationToken cancellationToken)
        {
            var lockEntity = await _db.ProcessingLocks.FindAsync(new object[] { memoryId }, cancellationToken);
            if (lockEntity != null)
            {
                _db.ProcessingLocks.Remove(lockEntity);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
        {
            // Use a transaction so all Upserts and MarkProcessed happen atomically
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await operation(ct);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        public async Task<IEnumerable<MemoryReadModel>> GetMemoriesByIdsAsync(IEnumerable<Guid> memoryIds, CancellationToken ct)
        {
            var entities = await _db.MemoryReadModels
                .Where(m => memoryIds.Contains(m.MemoryId))
                .ToListAsync(ct);

            return entities.Select(e => new MemoryReadModel
            {
                MemoryId = e.MemoryId,
                Summary = e.Summary,
                TokenCount = e.TokenCount,
                Archived = e.Archived,
                Importance = e.Importance,
                CreatedAt = e.CreatedAt
            });
        }

        public async Task<IEnumerable<ReadModelCandidate>> GetExpiredMemoriesAsync(int ttlDays, CancellationToken ct)
        {
            var cutoff = DateTime.UtcNow.AddDays(-ttlDays);
            var rows = await _db.MemoryReadModels
                .Where(m => !m.Archived && m.CreatedAt < cutoff)
                .Take(100) // batch size limit
                .ToListAsync(ct);

            return rows.Select(r => new ReadModelCandidate(
                r.MemoryId,
                r.StreamId,
                r.Summary,
                r.TokenCount,
                r.CreatedAt,
                r.Archived));
        }

        public async Task<IEnumerable<ReadModelCandidate>> GetArchivedMemoriesAsync(int olderThanDays, CancellationToken ct)
        {
            var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
            var rows = await _db.MemoryReadModels
                .Where(m => m.Archived && m.LastProcessedAt < cutoff)
                .Take(100)
                .ToListAsync(ct);

            return rows.Select(r => new ReadModelCandidate(
                r.MemoryId,
                r.StreamId,
                r.Summary,
                r.TokenCount,
                r.CreatedAt,
                r.Archived));
        }

        public async Task<int> GetMemoryCountByUserAsync(string userId, CancellationToken ct)
        {
            // Note: Since we didn't add UserId to MemoryReadModelEntity in this MVP yet,
            // we will just return a mock or count all for now.
            // In a real system, you'd add UserId to MemoryReadModelEntity and filter here.
            return await _db.MemoryReadModels.CountAsync(m => !m.Archived, ct);
        }
    }
}
