using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Persistence
{
    /// <summary>
    /// Pure read model persistence contract for memory summaries.
    /// Follows ISP by segregating query/storage operations from idempotency, locking, and maintenance concerns.
    /// </summary>
    public interface IReadModelRepository
    {
        /// <summary>
        /// Insert or update a read model entry.
        /// </summary>
        Task UpsertAsync(MemoryReadModel model, CancellationToken ct);

        /// <summary>
        /// Retrieves multiple read models by their IDs.
        /// </summary>
        Task<IEnumerable<MemoryReadModel>> GetMemoriesByIdsAsync(IEnumerable<Guid> memoryIds, CancellationToken ct);

        /// <summary>
        /// Retrieves all read models for a specific user (used for GDPR erasure / offboarding).
        /// </summary>
        Task<IEnumerable<MemoryReadModel>> GetMemoriesByUserAsync(string userId, CancellationToken ct);

        /// <summary>
        /// Gets the total number of memories for a user.
        /// </summary>
        Task<int> GetMemoryCountByUserAsync(string userId, CancellationToken ct);
    }
}
