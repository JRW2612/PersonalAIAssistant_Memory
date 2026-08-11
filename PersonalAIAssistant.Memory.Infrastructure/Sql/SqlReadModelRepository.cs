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
                DecryptSummary(r.Summary, r.IsEncrypted, r.UserId),
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
            if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                await operation(ct);
                await _db.SaveChangesAsync(ct);
                return;
            }

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
                UserId = e.UserId,
                Summary = DecryptSummary(e.Summary, e.IsEncrypted, e.UserId),
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

        public async Task<int> GetMemoryCountByUserAsync(string userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userId) || userId == "default")
            {
                return await _db.MemoryReadModels.CountAsync(m => !m.Archived, ct);
            }
            return await _db.MemoryReadModels.CountAsync(m => !m.Archived && m.UserId == userId, ct);
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
                // Fallback to ciphertext or empty string if decryption fails (e.g. key rotation/mismatch)
                return summary;
            }
        }
    }
}
