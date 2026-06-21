

using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Infrastructure.Mongo
{
    public interface IEventStore
    {
        Task AppendEventAsync(MemoryEvent memoryEvent, string streamId, CancellationToken cancellationToken);
        Task<IReadOnlyList<MemoryEvent>> GetEventsAsync(string streamId, CancellationToken cancellationToken);
    }
}
