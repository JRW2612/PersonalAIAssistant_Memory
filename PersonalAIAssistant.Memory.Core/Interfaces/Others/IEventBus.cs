using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    public interface IEventBus
    {
        /// <summary>
        /// Publish a single domain event asynchronously.
        /// </summary>
        Task PublishAsync(MemoryEvent evt, CancellationToken ct);

        /// <summary>
        /// Publish a batch of domain events asynchronously.
        /// </summary>
        Task PublishAsync(IEnumerable<MemoryEvent> events, CancellationToken ct);
    }
}
