using MassTransit;
using PersonalAIAssistant.Memory.Events;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
                // Note: MassTransit supports batch publishing, but for simplicity we iterate.
                // Depending on volume, BatchPublish is preferred.
                await _publishEndpoint.Publish((object)@event, cancellationToken);
            }
        }
    }
}
