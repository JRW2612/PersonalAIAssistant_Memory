using MassTransit;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Core.Observability;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Infrastructure.Events
{
    public class RabbitMQEventBus : IEventBus
    {
        private readonly IPublishEndpoint _publishEndpoint;

        public RabbitMQEventBus(IPublishEndpoint publishEndpoint)
        {
            _publishEndpoint = publishEndpoint;
        }

        public async Task PublishAsync(MemoryEvent evt, CancellationToken cancellationToken = default)
        {
            EnrichTraceparent(evt);
            await _publishEndpoint.Publish(evt, sendContext =>
            {
                if (!string.IsNullOrEmpty(evt.Traceparent))
                {
                    sendContext.Headers.Set(W3CTraceContext.HeaderName, evt.Traceparent);
                }
            }, cancellationToken);
        }

        public async Task PublishAsync(IEnumerable<MemoryEvent> events, CancellationToken cancellationToken = default)
        {
            foreach (var @event in events)
            {
                await PublishAsync(@event, cancellationToken);
            }
        }

        private static void EnrichTraceparent(MemoryEvent evt)
        {
            if (string.IsNullOrEmpty(evt.Traceparent))
            {
                var tp = W3CTraceContext.GetOrCreate(System.Diagnostics.Activity.Current?.Id);
                evt.Traceparent = tp.ToString();
            }
        }
    }
}
