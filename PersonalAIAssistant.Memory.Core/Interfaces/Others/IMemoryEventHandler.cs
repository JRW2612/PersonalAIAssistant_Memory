using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
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
