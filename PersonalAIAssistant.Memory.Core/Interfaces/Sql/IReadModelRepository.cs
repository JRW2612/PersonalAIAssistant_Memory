using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Sql
{
    public interface IReadModelRepository
    {
        /// <summary>
        /// Insert or update a read model entry.
        /// </summary>
        Task UpsertAsync(MemoryReadModel model, CancellationToken ct);

        /// <summary>
        /// Check if a given event version has already been processed for idempotency.
        /// </summary>
        Task<bool> HasProcessedAsync(Guid aggregateId, int version, CancellationToken ct);

        /// <summary>
        /// Mark an event version as processed.
        /// </summary>
        Task MarkProcessedAsync(Guid aggregateId, int version, CancellationToken ct);

        /// <summary>
        /// Get candidates for consolidation (e.g., large or old memories).
        /// </summary>
        Task<IEnumerable<ReadModelCandidate>> GetConsolidationCandidatesAsync(int batchSize, CancellationToken ct);

        /// <summary>
        /// Try to mark a candidate as "processing" to avoid duplicate work.
        /// Returns false if already being processed.
        /// </summary>
        Task<bool> TryMarkProcessingAsync(Guid memoryId, CancellationToken ct);

        /// <summary>
        /// Mark candidate as processed successfully.
        /// </summary>
        Task MarkProcessedAsync(Guid memoryId, CancellationToken ct);

        /// <summary>
        /// Unmark candidate if processing failed or was aborted.
        /// </summary>
        Task UnmarkProcessingAsync(Guid memoryId, CancellationToken ct);

        /// <summary>
        /// Retrieves multiple read models by their IDs.
        /// </summary>
        Task<IEnumerable<MemoryReadModel>> GetMemoriesByIdsAsync(IEnumerable<Guid> memoryIds, CancellationToken ct);

        /// <summary>
        /// Gets memories that have exceeded their TTL.
        /// </summary>
        Task<IEnumerable<ReadModelCandidate>> GetExpiredMemoriesAsync(int ttlDays, CancellationToken ct);

        /// <summary>
        /// Gets memories that have been archived longer than the threshold.
        /// </summary>
        Task<IEnumerable<ReadModelCandidate>> GetArchivedMemoriesAsync(int olderThanDays, CancellationToken ct);

        /// <summary>
        /// Gets the total number of memories for a user.
        /// </summary>
        Task<int> GetMemoryCountByUserAsync(string userId, CancellationToken ct);
    }
}
