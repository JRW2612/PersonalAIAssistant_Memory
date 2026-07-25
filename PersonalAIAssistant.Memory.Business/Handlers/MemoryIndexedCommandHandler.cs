using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Interfaces.Mongo;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    /// <summary>
    /// Handles <see cref="MemoryIndexedCommand"/> by rehydrating the aggregate and recording
    /// the vector-indexing fact as a <c>MemoryIndexedEvent</c> in the event stream.
    /// This is dispatched by <c>EmbeddingIndexingEventHandler</c> after a successful embedding upsert.
    /// </summary>
    public class MemoryIndexedCommandHandler : IRequestHandler<MemoryIndexedCommand, bool>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;

        public MemoryIndexedCommandHandler(IEventStore eventStore, IEventBus eventBus)
        {
            _eventStore = eventStore;
            _eventBus = eventBus;
        }

        public async Task<bool> Handle(MemoryIndexedCommand request, CancellationToken cancellationToken)
        {
            var streamId = $"memory-{request.MemoryId}";
            var history = await _eventStore.GetEventsAsync(streamId, cancellationToken);

            if (history == null || !history.Any())
                throw new KeyNotFoundException($"Memory with ID {request.MemoryId} not found.");

            var aggregate = new MemoryAggregate(new MemoryId(request.MemoryId));
            aggregate.LoadFromHistory(history);

            aggregate.MarkIndexed(request.EmbeddingId, request.VectorProvider, userId: "system");

            var uncommittedEvents = aggregate.UncommittedEvents.ToList();
            if (!uncommittedEvents.Any())
                return true;

            var expectedVersion = aggregate.Version - uncommittedEvents.Count;
            await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, expectedVersion, cancellationToken);
            await _eventBus.PublishAsync(uncommittedEvents, cancellationToken);
            aggregate.ClearUncommittedEvents();

            return true;
        }
    }
}
