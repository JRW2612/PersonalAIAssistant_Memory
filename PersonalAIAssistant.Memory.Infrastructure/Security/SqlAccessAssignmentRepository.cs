using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Infrastructure.Context;
using PersonalAIAssistant.Memory.Infrastructure.EF.Entities;

namespace PersonalAIAssistant.Memory.Infrastructure.Security;

public class SqlAccessAssignmentRepository : IAccessAssignmentRepository
{
    private readonly ReadModelDbContext _db;

    public SqlAccessAssignmentRepository(ReadModelDbContext db)
    {
        _db = db;
    }

    public async Task<AccessAssignmentEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.AccessAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<AccessAssignmentEntity?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        return await _db.AccessAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.IdempotencyKey == idempotencyKey, ct);
    }

    public async Task<bool> CreateAssignmentWithOutboxAsync(
        AccessAssignmentEntity assignment, 
        string eventType, 
        string payload, 
        CancellationToken ct = default)
    {
        var outboxMessage = new EfOutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageId = assignment.Id,
            MessageType = eventType,
            Payload = payload,
            OccurredAt = DateTime.UtcNow,
            Attempts = 0
        };

        // If running against in-memory database (tests or demo), save directly
        if (_db.Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true)
        {
            _db.AccessAssignments.Add(assignment);
            _db.OutboxMessages.Add(outboxMessage);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // Atomically commit assignment and outbox message in a single database transaction
        var executionStrategy = _db.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                _db.AccessAssignments.Add(assignment);
                _db.OutboxMessages.Add(outboxMessage);

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task UpdateAsync(AccessAssignmentEntity assignment, CancellationToken ct = default)
    {
        var local = _db.AccessAssignments.Local.FirstOrDefault(entry => entry.Id == assignment.Id);
        if (local != null && !ReferenceEquals(local, assignment))
        {
            _db.Entry(local).CurrentValues.SetValues(assignment);
        }
        else
        {
            _db.AccessAssignments.Update(assignment);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AccessAssignmentEntity>> GetStaleOrInDoubtAssignmentsAsync(
        TimeSpan staleAge, 
        int limit = 50, 
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.Subtract(staleAge);
        return await _db.AccessAssignments
            .Where(a => a.Status == AssignmentStatus.RequiresReconciliation || 
                       (a.Status == AssignmentStatus.Provisioning && a.CreatedAtUtc < cutoff))
            .OrderBy(a => a.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);
    }
}
