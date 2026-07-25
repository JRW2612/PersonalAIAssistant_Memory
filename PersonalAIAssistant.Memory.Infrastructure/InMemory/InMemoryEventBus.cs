using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Infrastructure.InMemory
{
    public class InMemoryEventBus : IEventBus
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<InMemoryEventBus> _logger;

        public InMemoryEventBus(IServiceScopeFactory scopeFactory, ILogger<InMemoryEventBus> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task PublishAsync(MemoryEvent evt, CancellationToken ct)
        {
            if (evt == null) return;

            _logger.LogInformation("Event published: {EventType} (AggregateId={AggregateId}, Version={Version})",
                evt.EventType, evt.AggregateId, evt.Version);

            using var scope = _scopeFactory.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IMemoryEventHandler>();

            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(evt, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing event handler {HandlerType} on event {EventType} for AggregateId={AggregateId}",
                        handler.GetType().Name, evt.EventType, evt.AggregateId);
                    throw;
                }
            }
        }

        public async Task PublishAsync(IEnumerable<MemoryEvent> events, CancellationToken ct)
        {
            if (events == null) return;
            var eventList = events.ToList();
            if (!eventList.Any()) return;

            foreach (var evt in eventList)
            {
                _logger.LogInformation("Event published: {EventType} (AggregateId={AggregateId}, Version={Version})",
                    evt?.EventType, evt?.AggregateId, evt?.Version);
            }

            using var scope = _scopeFactory.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IMemoryEventHandler>();

            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(eventList, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing event handler {HandlerType} on batch of {Count} events",
                        handler.GetType().Name, eventList.Count);
                    throw;
                }
            }
        }
    }
}
