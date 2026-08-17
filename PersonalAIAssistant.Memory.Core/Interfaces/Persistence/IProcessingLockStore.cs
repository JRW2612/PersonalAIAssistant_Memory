namespace PersonalAIAssistant.Memory.Core.Interfaces.Persistence
{
    /// <summary>
    /// Contract for worker distributed concurrency locks.
    /// </summary>
    public interface IProcessingLockStore
    {
        /// <summary>
        /// Try to mark a candidate as "processing" to avoid duplicate work.
        /// Returns false if already being processed.
        /// </summary>
        Task<bool> TryMarkProcessingAsync(Guid memoryId, CancellationToken ct);

        /// <summary>
        /// Mark candidate as processed successfully (releases lock).
        /// </summary>
        Task MarkProcessedAsync(Guid memoryId, CancellationToken ct);

        /// <summary>
        /// Unmark candidate if processing failed or was aborted.
        /// </summary>
        Task UnmarkProcessingAsync(Guid memoryId, CancellationToken ct);
    }
}
