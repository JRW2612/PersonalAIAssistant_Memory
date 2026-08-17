namespace PersonalAIAssistant.Memory.Core.Interfaces.Persistence
{
    /// <summary>
    /// Contract for tracking processed event versions to ensure idempotent event projection.
    /// </summary>
    public interface IEventIdempotencyStore
    {
        /// <summary>
        /// Check if a given event version has already been processed for idempotency.
        /// </summary>
        Task<bool> HasProcessedAsync(Guid aggregateId, int version, CancellationToken ct);

        /// <summary>
        /// Mark an event version as processed.
        /// </summary>
        Task MarkProcessedAsync(Guid aggregateId, int version, CancellationToken ct);
    }
}
