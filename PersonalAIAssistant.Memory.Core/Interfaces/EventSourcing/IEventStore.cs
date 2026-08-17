using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing
{
    /// <summary>
    /// Abstraction for event store operations in CQRS + Event Sourcing.
    /// Keeps Core independent of persistence technology.
    /// </summary>
    public interface IEventStore
    {
        /// <summary>Append a single event to a stream with optimistic concurrency.</summary>
        Task AppendEventAsync(
            string streamId,
            MemoryEvent memoryEvent,
            int expectedVersion,
            CancellationToken ct);

        /// <summary>Append multiple events to a stream with optimistic concurrency.</summary>
        Task AppendEventsAsync(
            string streamId,
            IReadOnlyList<MemoryEvent> events,
            int expectedVersion,
            CancellationToken ct);

        /// <summary>Get all events for a stream, ordered by version ascending.</summary>
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

        /// <summary>Returns the highest event version recorded for a stream (0 if no events exist).</summary>
        Task<int> GetCurrentVersionAsync(string streamId, CancellationToken ct);

        /// <summary>
        /// Returns all distinct stream IDs and their current (highest) event version,
        /// ordered by version descending, up to <paramref name="limit"/> entries.
        /// Used by the snapshot worker to find streams that need a new snapshot.
        /// </summary>
        Task<IReadOnlyList<(string StreamId, int CurrentVersion)>> GetStreamSummariesAsync(
            int limit,
            CancellationToken ct);
    }
}
