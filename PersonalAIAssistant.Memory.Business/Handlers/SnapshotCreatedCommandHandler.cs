using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    public class SnapshotCreatedCommandHandler : IRequestHandler<SnapshotCreatedCommand, Guid>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;
        private readonly ISnapshotRepository _snapshotRepo;

        public SnapshotCreatedCommandHandler(
            IEventStore eventStore,
            IEventBus eventBus,
            ISnapshotRepository snapshotRepo)
        {
            _eventStore = eventStore;
            _eventBus = eventBus;
            _snapshotRepo = snapshotRepo;
        }

        public async Task<Guid> Handle(SnapshotCreatedCommand request, CancellationToken cancellationToken)
        {
            var streamId = $"memory-{request.AggregateIdSnapshot}";
            var history = await _eventStore.GetEventsAsync(streamId, cancellationToken);

            if (history == null || !history.Any())
                throw new KeyNotFoundException($"Memory with ID {request.AggregateIdSnapshot} not found.");

            var aggregate = new MemoryAggregate(new MemoryId(request.AggregateIdSnapshot));
            aggregate.LoadFromHistory(history);

            await _snapshotRepo.SaveSnapshotAsync(
                streamId, request.SnapshotPayload, request.SnapshotVersion, cancellationToken);

            aggregate.CreateSnapshot(request.SnapshotPayload, request.SnapshotVersion, userId: "system");

            var uncommittedEvents = aggregate.UncommittedEvents.ToList();
            if (uncommittedEvents.Any())
            {
                var expectedVersion = aggregate.Version - uncommittedEvents.Count;
                await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, expectedVersion, cancellationToken);
                await _eventBus.PublishAsync(uncommittedEvents, cancellationToken);
                aggregate.ClearUncommittedEvents();
            }

            return request.AggregateIdSnapshot;
        }
    }
}
