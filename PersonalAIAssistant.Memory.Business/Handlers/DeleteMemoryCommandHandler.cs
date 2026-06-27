using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    public class DeleteMemoryCommandHandler : IRequestHandler<DeleteMemoryCommand, Unit> // Or Guid if you return the ID
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;

        public DeleteMemoryCommandHandler(IEventStore eventStore, IEventBus eventBus)
        {
            _eventStore = eventStore;
            _eventBus = eventBus;
        }

        public async Task<Unit> Handle(DeleteMemoryCommand request, CancellationToken cancellationToken)
        {
            var streamId = $"memory-{request.MemoryId}";

            // 1. Fetch historical events from the Event Store
            // NOTE: Replace 'GetEventsAsync' with the actual read method on your IEventStore interface
            var history = await _eventStore.GetEventsAsync(streamId, cancellationToken);

            if (history == null || !history.Any())
            {
                throw new KeyNotFoundException($"Memory with ID {request.MemoryId} not found.");
            }

            // 2. Rehydrate the aggregate
            var aggregate = new MemoryAggregate(new MemoryId(request.MemoryId));
            aggregate.LoadFromHistory(history);

            // 3. Apply Domain Logic
            aggregate.Delete(request.Reason, request.UserId);

            var uncommittedEvents = aggregate.UncommittedEvents.ToList();
            if (!uncommittedEvents.Any())
            {
                return Unit.Value; // Nothing changed (e.g., it was already deleted)
            }

            // 4. Persist new events
            // The expected version is usually the aggregate's current version MINUS the number of new events
            int expectedVersion = aggregate.Version - uncommittedEvents.Count;
            await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, expectedVersion, cancellationToken);

            // 5. Publish to bus
            foreach (var evt in uncommittedEvents)
            {
                await _eventBus.PublishAsync(evt, cancellationToken);
            }

            aggregate.ClearUncommittedEvents();
            return Unit.Value;
        }
    }
}