using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;

namespace PersonalAIAssistant.Memory.Business.Handlers;

public class MemoryIndexedCommandHandler : IRequestHandler<MemoryIndexedCommand, bool>
{
    private readonly IEventStore _eventStore;

    public MemoryIndexedCommandHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
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
        var outboxMessages = EventOutboxMessageFactory.Create(uncommittedEvents);
        await _eventStore.AppendEventsWithOutboxAsync(streamId, uncommittedEvents, expectedVersion, outboxMessages, cancellationToken);
        aggregate.ClearUncommittedEvents();

        return true;
    }
}
