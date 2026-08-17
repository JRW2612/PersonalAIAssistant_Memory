using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Messaging
{
    /// <summary>
    /// Generic handler contract for subscribing to a specific domain event type.
    /// Follows ISP by allowing event handlers to depend only on the events they handle.
    /// </summary>
    public interface IMemoryEventHandler<in TEvent> where TEvent : MemoryEvent
    {
        Task HandleAsync(TEvent evt, CancellationToken ct);
    }

    /// <summary>
    /// Non-generic handler contract for projectors and cross-cutting event subscribers.
    /// </summary>
    public interface IMemoryEventHandler
    {
        Task HandleAsync(MemoryEvent evt, CancellationToken ct);

        async Task HandleAsync(IEnumerable<MemoryEvent> events, CancellationToken ct)
        {
            if (events == null) return;
            foreach (var evt in events)
            {
                ct.ThrowIfCancellationRequested();
                await HandleAsync(evt, ct);
            }
        }
    }
}
