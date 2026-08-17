using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    public class UpdateMemoryCommandHandler : IRequestHandler<UpdateMemoryCommand, Guid>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;

        public UpdateMemoryCommandHandler(IEventStore eventStore, IEventBus eventBus)
        {
            _eventStore = eventStore;
            _eventBus = eventBus;
        }

        public async Task<Guid> Handle(UpdateMemoryCommand request, CancellationToken cancellationToken)
        {
            var streamId = $"memory-{request.MemoryId}";
            var history = await _eventStore.GetEventsAsync(streamId, cancellationToken);

            if (history == null || !history.Any())
                throw new KeyNotFoundException($"Memory with ID {request.MemoryId} not found.");

            var aggregate = new MemoryAggregate(new MemoryId(request.MemoryId));
            aggregate.LoadFromHistory(history);

            if (request.UpdatedFields != null &&
                request.UpdatedFields.TryGetValue(nameof(MemoryAggregate.RawText), out var newText))
            {
                aggregate.UpdateRawText(newText, request.UserId);
            }

            var uncommittedEvents = aggregate.UncommittedEvents.ToList();
            if (!uncommittedEvents.Any())
                return aggregate.Id.Value;

            var expectedVersion = aggregate.Version - uncommittedEvents.Count;
            await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, expectedVersion, cancellationToken);
            await _eventBus.PublishAsync(uncommittedEvents, cancellationToken);
            aggregate.ClearUncommittedEvents();

            return aggregate.Id.Value;
        }
    }
}
