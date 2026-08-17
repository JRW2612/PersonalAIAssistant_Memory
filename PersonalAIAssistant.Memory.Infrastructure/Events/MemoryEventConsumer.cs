using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Events;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using System;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Infrastructure.Events
{
    public class MemoryEventConsumer<TEvent> : IConsumer<TEvent> where TEvent : MemoryEvent
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
            var memoryEvent = context.Message;
            if (memoryEvent == null) return;

            _logger.LogDebug("Consuming event {EventType} for MemoryId {MemoryId}", memoryEvent.GetType().Name, memoryEvent.AggregateId);

            using var scope = _serviceProvider.CreateScope();

            // 1. Untyped handlers (projectors)
            var handlers = scope.ServiceProvider.GetServices<IMemoryEventHandler>();
            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(memoryEvent, context.CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling event {EventType} by untyped handler {HandlerType}", context.Message.GetType().Name, handler.GetType().Name);
                    throw;
                }
            }

            // 2. Typed handlers (IMemoryEventHandler<TEvent>)
            var typedHandlers = scope.ServiceProvider.GetServices<IMemoryEventHandler<TEvent>>();
            foreach (var handler in typedHandlers)
            {
                try
                {
                    await handler.HandleAsync(context.Message, context.CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling event {EventType} by typed handler {HandlerType}", context.Message.GetType().Name, handler.GetType().Name);
                    throw;
                }
            }
        }
    }
}
