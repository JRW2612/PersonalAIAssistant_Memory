using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;

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


            var memoryId = MemoryId.New();

            if (!Enum.TryParse<MemorySource>(request.Source, ignoreCase: true, out var source) ||
                !Enum.IsDefined(source))
            {
                throw new DomainException($"Unsupported memory source found'{request.Source}'.");
            }

            //call domain method to add memory
            aggregate.AddMemory(
                      rawText: request.RawText,
                      source: Enum.Parse<MemorySource>(request.Source, ignoreCase: true), // Map the string to your enum,
                      tags: request.Tags,
                      userId: request.UserId,
                      correlationId: request.CorrelationId
                      );

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


            // Publish to bus
            foreach (var evt in uncommittedEvents)
            {
                await _eventBus.PublishAsync(evt, cancellationToken);
            }

            //Clean up aggregate state
            aggregate.ClearUncommittedEvents();

            return aggregate.Id.Value;
        }
    }
}
