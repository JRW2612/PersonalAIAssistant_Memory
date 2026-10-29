using PersonalAIAssistant.Memory.Core.Entities;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Security;

public interface IAccessAssignmentRepository
{
    Task<AccessAssignmentEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AccessAssignmentEntity?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task<bool> CreateAssignmentWithOutboxAsync(AccessAssignmentEntity assignment, string eventType, string payload, CancellationToken ct = default);
    Task UpdateAsync(AccessAssignmentEntity assignment, CancellationToken ct = default);
    Task<IReadOnlyList<AccessAssignmentEntity>> GetStaleOrInDoubtAssignmentsAsync(TimeSpan staleAge, int limit = 50, CancellationToken ct = default);
}
