using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Messaging
{
    public interface IEventBus
    {
        Task PublishAsync(MemoryEvent evt, CancellationToken cancellationToken = default);
        Task PublishAsync(IEnumerable<MemoryEvent> events, CancellationToken cancellationToken = default);
    }
}
