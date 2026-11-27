using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;

namespace PersonalAIAssistant.Memory.Business.Handlers;

public class UpdateMemoryCommandHandler : IRequestHandler<UpdateMemoryCommand, Guid>
{
    private readonly IEventStore _eventStore;

    public UpdateMemoryCommandHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<Guid> Handle(UpdateMemoryCommand request, CancellationToken cancellationToken)
    {
        var streamId = $"memory-{request.MemoryId}";
        var history = await _eventStore.GetEventsAsync(streamId, cancellationToken);

        if (history == null || !history.Any())
            throw new KeyNotFoundException($"Memory with ID {request.MemoryId} not found.");

        // BOLA / IDOR Defense: verify memory ownership
        var ownerUserId = history.Select(e => e.UserId).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        if (!string.IsNullOrWhiteSpace(ownerUserId) &&
            !string.IsNullOrWhiteSpace(request.UserId) &&
            !string.Equals(request.UserId, "system", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ownerUserId, request.UserId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Memory '{request.MemoryId}' does not belong to user '{request.UserId}'.");
        }

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
        var outboxMessages = EventOutboxMessageFactory.Create(uncommittedEvents);
        await _eventStore.AppendEventsWithOutboxAsync(streamId, uncommittedEvents, expectedVersion, outboxMessages, cancellationToken);
        aggregate.ClearUncommittedEvents();

        return aggregate.Id.Value;
    }
}
