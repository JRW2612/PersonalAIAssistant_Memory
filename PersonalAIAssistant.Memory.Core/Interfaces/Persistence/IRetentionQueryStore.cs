using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Persistence
{
    /// <summary>
    /// Contract for maintenance, retention, and consolidation candidate search queries.
    /// </summary>
    public interface IRetentionQueryStore
    {
        /// <summary>
        /// Get candidates for consolidation (e.g., large or old memories).
        /// </summary>
        Task<IEnumerable<ReadModelCandidate>> GetConsolidationCandidatesAsync(int batchSize, CancellationToken ct);

        /// <summary>
        /// Gets memories that have exceeded their TTL.
        /// </summary>
        Task<IEnumerable<ReadModelCandidate>> GetExpiredMemoriesAsync(int ttlDays, CancellationToken ct);

        /// <summary>
        /// Gets memories that have been archived longer than the threshold.
        /// </summary>
        Task<IEnumerable<ReadModelCandidate>> GetArchivedMemoriesAsync(int olderThanDays, CancellationToken ct);
    }
}
