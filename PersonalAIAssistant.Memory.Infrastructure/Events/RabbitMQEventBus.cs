using MassTransit;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
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
            await _publishEndpoint.Publish(evt, cancellationToken);
        }

        public async Task PublishAsync(IEnumerable<MemoryEvent> events, CancellationToken cancellationToken = default)
        {
            foreach (var @event in events)
            {
                await _publishEndpoint.Publish((object)@event, cancellationToken);
            }
        }
    }
}
