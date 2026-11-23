using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;

namespace PersonalAIAssistant.Memory.Business.Handlers;

public class SnapshotCreatedCommandHandler : IRequestHandler<SnapshotCreatedCommand, Guid>
{
    private readonly IEventStore _eventStore;
    private readonly ISnapshotRepository _snapshotRepo;

    public SnapshotCreatedCommandHandler(
        IEventStore eventStore,
        ISnapshotRepository snapshotRepo)
    {
        _eventStore = eventStore;
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
            var outboxMessages = EventOutboxMessageFactory.Create(uncommittedEvents);
            await _eventStore.AppendEventsWithOutboxAsync(streamId, uncommittedEvents, expectedVersion, outboxMessages, cancellationToken);
            aggregate.ClearUncommittedEvents();
        }

        return request.AggregateIdSnapshot;
    }
}
