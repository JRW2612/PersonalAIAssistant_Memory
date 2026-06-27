using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;
// using System.Drawing; // Removed: You shouldn't need UI/Drawing namespaces in your Business layer

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
            // 1. Instantiate the new aggregate
            var aggregate = new MemoryAggregate();

            // 2. APPLY DOMAIN LOGIC (CRITICAL FIX)
            // You must call a method on your aggregate and pass the data from the request.
            // Note: Replace '.CreateNewMemory(...)' with whatever method actually exists on your aggregate.
            var memoryId = MemoryId.New();


            // 2. Call the domain method you defined
            aggregate.AddMemory(
                      rawText: request.RawText, // <-- Fixed: Changed from request.Content
                      source: Enum.Parse<MemorySource>(request.Source), // Map the string to your enum
                      tags: request.Tags,
                      userId: request.UserId,
                      correlationId: request.CorrelationId
                      );

            var uncommittedEvents = aggregate.UncommittedEvents.ToList();

            // Safety check: If domain logic failed or generated no events, exit early.
            if (!uncommittedEvents.Any())
            {
                // Or throw a DomainException depending on your error handling strategy
                return aggregate.Id.Value;
            }

            var streamId = $"memory-{aggregate.Id.Value}";

            // 3. Persist events
            // Assuming '0' represents a new stream in your specific Event Store implementation
            await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, 0, cancellationToken);

            // 4. Publish to bus
            foreach (var evt in uncommittedEvents)
            {
                await _eventBus.PublishAsync(evt, cancellationToken);
            }

            // 5. Clean up aggregate state
            aggregate.ClearUncommittedEvents();

            return aggregate.Id.Value;
        }
    }
}