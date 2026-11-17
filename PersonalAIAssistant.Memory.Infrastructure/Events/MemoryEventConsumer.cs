using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Core.Observability;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Infrastructure.Events
{
    public class MemoryEventConsumer<TEvent> : IConsumer<TEvent> where TEvent : MemoryEvent
    {
        private static readonly ActivitySource ActivitySource = new("PersonalAIAssistant.Memory.Consumer");
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

            // Extract W3C traceparent header from message bus headers or event payload
            var rawTraceparent = context.Headers.Get<string>(W3CTraceContext.HeaderName) ?? memoryEvent.Traceparent;
            var traceContext = W3CTraceContext.GetOrCreate(rawTraceparent);
            memoryEvent.Traceparent = traceContext.ToString();

            using var activity = ActivitySource.StartActivity(
                $"Consume:{typeof(TEvent).Name}",
                ActivityKind.Consumer,
                ActivityContext.Parse(traceContext.ToString(), null));

            _logger.LogDebug(
                "Consuming event {EventType} for MemoryId {MemoryId} with traceparent {Traceparent}",
                memoryEvent.GetType().Name,
                memoryEvent.AggregateId,
                traceContext.ToString());

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
                    _logger.LogError(
                        ex,
                        "Error handling event {EventType} by untyped handler {HandlerType} with traceparent {Traceparent}",
                        context.Message.GetType().Name,
                        handler.GetType().Name,
                        traceContext.ToString());
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
                    _logger.LogError(
                        ex,
                        "Error handling event {EventType} by typed handler {HandlerType} with traceparent {Traceparent}",
                        context.Message.GetType().Name,
                        handler.GetType().Name,
                        traceContext.ToString());
                    throw;
                }
            }
        }
    }
}
