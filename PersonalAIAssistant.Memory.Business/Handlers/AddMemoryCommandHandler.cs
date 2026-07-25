using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Interfaces.Mongo;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    public class AddMemoryCommandHandler : IRequestHandler<AddMemoryCommand, Guid>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;

        public AddMemoryCommandHandler(IEventStore eventStore, IEventBus eventBus)
        {
            _eventStore = eventStore;
            _eventBus = eventBus;
        }

        public async Task<Guid> Handle(AddMemoryCommand request, CancellationToken cancellationToken)
        {
            var aggregate = new MemoryAggregate();

            if (!Enum.TryParse<MemorySource>(request.Source, ignoreCase: true, out var source) || !Enum.IsDefined(source))
                throw new DomainException($"Unsupported memory source: '{request.Source}'.");

            aggregate.AddMemory(
                rawText: request.RawText,
                source: source,
                importance: request.Importance,
                tags: request.Tags,
                userId: request.UserId,
                correlationId: request.CorrelationId);

            var uncommittedEvents = aggregate.UncommittedEvents.ToList();
           // If domain logic failed or generated no events, exit early.
            if (!uncommittedEvents.Any())
            {
                return aggregate.Id.Value;
            }

            var streamId = $"memory-{aggregate.Id.Value}";

            //  Persisting events
            // Assuming '0' represents a new stream in your specific Event Store implementation
            await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, 0, cancellationToken);


            // Publish all events in one batch
            await _eventBus.PublishAsync(uncommittedEvents, cancellationToken);

            //Clean up aggregate state
            aggregate.ClearUncommittedEvents();

            return aggregate.Id.Value;
        }
    }
}
