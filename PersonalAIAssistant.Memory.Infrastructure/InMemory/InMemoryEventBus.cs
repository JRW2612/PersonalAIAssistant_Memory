using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Infrastructure.InMemory
{
    public class InMemoryEventBus : IEventBus
    {
        private readonly ILogger<InMemoryEventBus> _logger;

        public InMemoryEventBus(ILogger<InMemoryEventBus> logger)
        {
            _logger = logger;
        }

        public Task PublishAsync(MemoryEvent evt, CancellationToken ct)
        {
            _logger.LogInformation("Event published: {EventType} (AggregateId={AggregateId}, Version={Version})",
                evt.EventType, evt.AggregateId, evt.Version);
            return Task.CompletedTask;
        }

        public Task PublishAsync(IEnumerable<MemoryEvent> events, CancellationToken ct)
        {
            foreach (var evt in events)
            {
                _logger.LogInformation("Event published: {EventType} (AggregateId={AggregateId}, Version={Version})",
                    evt.EventType, evt.AggregateId, evt.Version);
            }
            return Task.CompletedTask;
        }
    }
}
