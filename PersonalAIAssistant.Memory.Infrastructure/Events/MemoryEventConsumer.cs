using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Events;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using System;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Infrastructure.Events
{
    public class MemoryEventConsumer<TEvent> : IConsumer<TEvent> where TEvent : class
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MemoryEventConsumer<TEvent>> _logger;

        public MemoryEventConsumer(IServiceProvider serviceProvider, ILogger<MemoryEventConsumer<TEvent>> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<TEvent> context)
        {
            var memoryEvent = context.Message as MemoryEvent;
            if (memoryEvent == null) return;

            _logger.LogDebug("Consuming event {EventType} for MemoryId {MemoryId}", memoryEvent.GetType().Name, memoryEvent.AggregateId);

            // We create a scope so that DbContexts and Repositories are properly scoped per message
            using var scope = _serviceProvider.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IMemoryEventHandler>();

            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(memoryEvent, context.CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling event {EventType} by handler {HandlerType}", context.Message.GetType().Name, handler.GetType().Name);
                    throw; // Rethrow to let MassTransit handle retries/DLQ
                }
            }
        }
    }
}
