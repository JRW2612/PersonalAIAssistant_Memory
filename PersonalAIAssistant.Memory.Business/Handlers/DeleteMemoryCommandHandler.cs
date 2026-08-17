using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    public class DeleteMemoryCommandHandler : IRequestHandler<DeleteMemoryCommand, Unit>
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

            var history = await _eventStore.GetEventsAsync(streamId, cancellationToken);
            if (history == null || !history.Any())
            {
                throw new KeyNotFoundException($"Memory with ID {request.MemoryId} not found.");
            }

            var aggregate = new MemoryAggregate(new MemoryId(request.MemoryId));
            aggregate.LoadFromHistory(history);

            aggregate.Delete(request.Reason, request.UserId);

            var uncommittedEvents = aggregate.UncommittedEvents.ToList();
            if (!uncommittedEvents.Any())
            {
                return Unit.Value;
            }

            int expectedVersion = aggregate.Version - uncommittedEvents.Count;
            await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, expectedVersion, cancellationToken);
            await _eventBus.PublishAsync(uncommittedEvents, cancellationToken);

            aggregate.ClearUncommittedEvents();
            return Unit.Value;
        }
    }
}