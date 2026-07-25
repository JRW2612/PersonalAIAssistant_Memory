using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Interfaces.Mongo;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    public class CompressMemoryCommandHandler : IRequestHandler<CompressMemoryCommand, Guid>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;

        public CompressMemoryCommandHandler(IEventStore eventStore, IEventBus eventBus)
        {
            _eventStore = eventStore;
            _eventBus = eventBus;
        }

        public async Task<Guid> Handle(CompressMemoryCommand request, CancellationToken cancellationToken)
        {
            var streamId = $"memory-{request.OriginalMemoryId}";

            var eventHistory = await _eventStore.GetEventsAsync(streamId, cancellationToken);
            if (eventHistory == null || !eventHistory.Any())
            {
                throw new KeyNotFoundException($"No events found for memory with ID {streamId}");
            }

            var aggregate = new MemoryAggregate(new MemoryId(request.OriginalMemoryId));
            aggregate.LoadFromHistory(eventHistory);

            aggregate.Compress(request.CompressedText, request.CompressionModel, request.TokenCount, request.UserId);

            var uncommittedEvents = aggregate.UncommittedEvents.ToList();
            if (!uncommittedEvents.Any())
            {
                return aggregate.Id.Value;
            }

            var expectedVersion = aggregate.Version - uncommittedEvents.Count;
            await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, expectedVersion, cancellationToken);

            // Publish all events in one batch
            await _eventBus.PublishAsync(uncommittedEvents, cancellationToken);

            aggregate.ClearUncommittedEvents();
            return aggregate.Id.Value;
        }
    }
}
