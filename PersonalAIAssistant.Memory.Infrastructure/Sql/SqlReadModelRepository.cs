using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.Context;

namespace PersonalAIAssistant.Memory.Infrastructure.Sql
{
    /// <summary>
    /// Implements segregated persistence contracts for SQL read models, idempotency, locks, and retention queries.
    /// </summary>
    public class SqlReadModelRepository :
        IReadModelRepository,
        IEventIdempotencyStore,
        IProcessingLockStore,
        IRetentionQueryStore,
        ITransactionalReadModelRepository,
        ITemporalFactRepository
    {
        private readonly ReadModelDbContext _db;
        private readonly IEncryptionService _encryptionService;
        private readonly Microsoft.Extensions.Options.IOptions<PersonalAIAssistant.Memory.Core.Models.EncryptionOptions> _encryptOptions;

        public SqlReadModelRepository(
            ReadModelDbContext db,
            IEncryptionService encryptionService,
            Microsoft.Extensions.Options.IOptions<PersonalAIAssistant.Memory.Core.Models.EncryptionOptions> encryptOptions)
        {
            _db = db;
            _encryptionService = encryptionService;
            _encryptOptions = encryptOptions;
        }

        // ─── IReadModelRepository ────────────────────────────────────────────────

        public async Task UpsertAsync(MemoryReadModel model, CancellationToken cancellationToken)
        {
            var summary = model.Summary;
            var isEncrypted = false;
            if (_encryptOptions.Value.Enabled && !string.IsNullOrEmpty(summary))
            {
                var userKey = _encryptOptions.Value.SystemKey + "_" + (model.UserId ?? "default");
                summary = _encryptionService.Encrypt(summary, userKey);
                isEncrypted = true;
            }

            var existing = await _db.MemoryReadModels.FindAsync(new object[] { model.MemoryId }, cancellationToken);
            if (existing == null)
            {
                var entity = new MemoryReadModelEntity
                {
                    MemoryId = model.MemoryId,
                    StreamId = $"memory-{model.MemoryId}",
                    UserId = model.UserId,
                    Summary = summary,
                    TokenCount = model.TokenCount,
                    Importance = model.Importance,
                    CreatedAt = model.CreatedAt != default ? model.CreatedAt : DateTime.UtcNow,
                    Archived = model.Archived,
                    LastProcessedAt = DateTime.UtcNow,
                    IsEncrypted = isEncrypted
                };
                _db.MemoryReadModels.Add(entity);
            }
            else
            {
                if (!string.IsNullOrEmpty(model.UserId)) existing.UserId = model.UserId;
                existing.Summary = summary;
                existing.TokenCount = model.TokenCount;
                existing.Importance = model.Importance;
                existing.Archived = model.Archived;
                existing.LastProcessedAt = DateTime.UtcNow;
                existing.IsEncrypted = isEncrypted;
                _db.MemoryReadModels.Update(existing);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task<IEnumerable<MemoryReadModel>> GetMemoriesByIdsAsync(IEnumerable<Guid> memoryIds, CancellationToken ct)
        {
            var entities = await _db.MemoryReadModels
                .Where(m => memoryIds.Contains(m.MemoryId))
                .ToListAsync(ct);

            return entities.Select(e => new MemoryReadModel
            {
                MemoryId = e.MemoryId,
                UserId = e.UserId,
                Summary = DecryptSummary(e.Summary, e.IsEncrypted, e.UserId),
                TokenCount = e.TokenCount,
                Archived = e.Archived,
                Importance = e.Importance,
                CreatedAt = e.CreatedAt
            });
        }

        public async Task<IEnumerable<MemoryReadModel>> GetMemoriesByUserAsync(string userId, CancellationToken ct)
        {
            var entities = await _db.MemoryReadModels
                .Where(m => m.UserId == userId)
                .ToListAsync(ct);

            return entities.Select(e => new MemoryReadModel
            {
                MemoryId = e.MemoryId,
                UserId = e.UserId,
                Summary = DecryptSummary(e.Summary, e.IsEncrypted, e.UserId),
                TokenCount = e.TokenCount,
                Archived = e.Archived,
                Importance = e.Importance,
                CreatedAt = e.CreatedAt
            });
        }

        public async Task<int> GetMemoryCountByUserAsync(string userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId) || userId == "default")
            {
                return await _db.MemoryReadModels.CountAsync(m => !m.Archived, ct);
            }
            return await _db.MemoryReadModels.CountAsync(m => !m.Archived && m.UserId == userId, ct);
        }

        // ─── IEventIdempotencyStore ──────────────────────────────────────────────

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

        // ─── IProcessingLockStore ────────────────────────────────────────────────

        public async Task<bool> TryMarkProcessingAsync(Guid memoryId, CancellationToken cancellationToken)
        {
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

        // ─── IRetentionQueryStore ────────────────────────────────────────────────

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
                DecryptSummary(r.Summary, r.IsEncrypted, r.UserId),
                r.TokenCount,
                r.CreatedAt,
                r.Archived));
        }

        public async Task<IEnumerable<ReadModelCandidate>> GetExpiredMemoriesAsync(int ttlDays, CancellationToken ct)
        {
            var cutoff = DateTime.UtcNow.AddDays(-ttlDays);
            var rows = await _db.MemoryReadModels
                .Where(m => !m.Archived && m.CreatedAt < cutoff)
                .Take(100)
                .ToListAsync(ct);

            return rows.Select(r => new ReadModelCandidate(
                r.MemoryId,
                r.StreamId,
                DecryptSummary(r.Summary, r.IsEncrypted, r.UserId),
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
                DecryptSummary(r.Summary, r.IsEncrypted, r.UserId),
                r.TokenCount,
                r.CreatedAt,
                r.Archived));
        }

        // ─── ITransactionalReadModelRepository ───────────────────────────────────

        public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
        {
            if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                await operation(ct);
                await _db.SaveChangesAsync(ct);
                return;
            }

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

        private string DecryptSummary(string summary, bool isEncrypted, string userId)
        {
            if (!isEncrypted || string.IsNullOrEmpty(summary)) return summary;
            try
            {
                var userKey = _encryptOptions.Value.SystemKey + "_" + (userId ?? "default");
                return _encryptionService.Decrypt(summary, userKey);
            }
            catch
            {
                return summary;
            }
        }

        // ─── ITemporalFactRepository ──────────────────────────────────────────────

        public async Task UpsertFactAsync(TemporalFact fact, string userId, CancellationToken ct = default)
        {
            var entity = await _db.TemporalFacts.FirstOrDefaultAsync(f => f.Id == fact.Id, ct);
            if (entity == null)
            {
                entity = new TemporalFactEntity
                {
                    Id = fact.Id,
                    UserId = userId,
                    Subject = fact.Subject,
                    Predicate = fact.Predicate,
                    Object = fact.Object,
                    ValidFromUtc = fact.ValidFromUtc,
                    ValidToUtc = fact.ValidToUtc,
                    IsCurrent = fact.IsCurrent,
                    Confidence = fact.Confidence,
                    Source = fact.Source,
                    CreatedAtUtc = DateTime.UtcNow
                };
                await _db.TemporalFacts.AddAsync(entity, ct);
            }
            else
            {
                entity.Object = fact.Object;
                entity.ValidFromUtc = fact.ValidFromUtc;
                entity.ValidToUtc = fact.ValidToUtc;
                entity.IsCurrent = fact.IsCurrent;
                entity.Confidence = fact.Confidence;
                entity.Source = fact.Source;
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task InvalidateFactAsync(string userId, string subject, string predicate, DateTime invalidatedAtUtc, CancellationToken ct = default)
        {
            var activeFacts = await _db.TemporalFacts
                .Where(f => f.UserId == userId && f.IsCurrent && f.Subject == subject && f.Predicate == predicate)
                .ToListAsync(ct);

            foreach (var fact in activeFacts)
            {
                fact.IsCurrent = false;
                fact.ValidToUtc = invalidatedAtUtc;
                fact.InvalidatedAtUtc = DateTime.UtcNow;
            }

            if (activeFacts.Any())
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        public async Task<IReadOnlyList<TemporalFact>> GetCurrentFactsAsync(string userId, string? subject = null, CancellationToken ct = default)
        {
            var query = _db.TemporalFacts.AsNoTracking()
                .Where(f => f.UserId == userId && f.IsCurrent);

            if (!string.IsNullOrWhiteSpace(subject))
            {
                query = query.Where(f => f.Subject == subject);
            }

            var entities = await query.OrderBy(f => f.Subject).ThenBy(f => f.Predicate).ToListAsync(ct);

            return entities.Select(e => new TemporalFact(
                Id: e.Id,
                Subject: e.Subject,
                Predicate: e.Predicate,
                Object: e.Object,
                ValidFromUtc: e.ValidFromUtc,
                ValidToUtc: e.ValidToUtc,
                IsCurrent: e.IsCurrent,
                Confidence: e.Confidence,
                Source: e.Source
            )).ToList();
        }

        public async Task<IReadOnlyList<TemporalFact>> GetFactHistoryAsync(string userId, string subject, string predicate, CancellationToken ct = default)
        {
            var entities = await _db.TemporalFacts.AsNoTracking()
                .Where(f => f.UserId == userId && f.Subject == subject && f.Predicate == predicate)
                .OrderByDescending(f => f.ValidFromUtc)
                .ToListAsync(ct);

            return entities.Select(e => new TemporalFact(
                Id: e.Id,
                Subject: e.Subject,
                Predicate: e.Predicate,
                Object: e.Object,
                ValidFromUtc: e.ValidFromUtc,
                ValidToUtc: e.ValidToUtc,
                IsCurrent: e.IsCurrent,
                Confidence: e.Confidence,
                Source: e.Source
            )).ToList();
        }
    }
}
