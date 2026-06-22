using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.Sql
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
    }
}
