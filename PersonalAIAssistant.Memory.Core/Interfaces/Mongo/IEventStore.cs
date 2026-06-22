

using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Infrastructure.Mongo
{
    /// <summary>
    /// Abstraction for event store operations in CQRS + Event Sourcing.
    /// Keeps Core independent of persistence technology.
    /// </summary>
    public interface IEventStore
    {
        /// <summary>
        /// Append a single event to a stream with optimistic concurrency.
        /// </summary>
        Task AppendEventAsync(
            string streamId,
            MemoryEvent memoryEvent,
            int expectedVersion,
            CancellationToken ct);

        /// <summary>
        /// Append multiple events to a stream with optimistic concurrency.
        /// </summary>
        Task AppendEventsAsync(
            string streamId,
            IReadOnlyList<MemoryEvent> events,
            int expectedVersion,
            CancellationToken ct);

        /// <summary>
        /// Get all events for a stream.
        /// </summary>
        Task<IReadOnlyList<MemoryEvent>> GetEventsAsync(
            string streamId,
            CancellationToken ct);

        /// <summary>
        /// Get events for a stream starting after a given version.
        /// Useful for replaying only tail events after a snapshot.
        /// </summary>
        Task<IReadOnlyList<MemoryEvent>> GetEventsFromVersionAsync(
            string streamId,
            int fromVersion,
            CancellationToken ct);
    }
}
