using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Business.Handlers;

public class AssertTemporalFactCommandHandler : IRequestHandler<AssertTemporalFactCommand, Guid>
{
    private readonly IEventStore _eventStore;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AssertTemporalFactCommandHandler> _logger;

    public AssertTemporalFactCommandHandler(
        IEventStore eventStore,
        IEventBus eventBus,
        ILogger<AssertTemporalFactCommandHandler> logger)
    {
        _eventStore = eventStore;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Guid> Handle(AssertTemporalFactCommand request, CancellationToken cancellationToken)
    {
        var streamId = $"temporal-facts-{request.UserId}";

        var history = await _eventStore.GetEventsAsync(streamId, cancellationToken);
        var aggregate = new MemoryAggregate();
        aggregate.LoadFromHistory(history);

        var expectedVersion = aggregate.Version;

        aggregate.AssertTemporalFact(
            subject: request.Subject,
            predicate: request.Predicate,
            @object: request.Object,
            validFromUtc: request.ValidFromUtc,
            confidence: request.Confidence,
            source: request.Source,
            userId: request.UserId);

        var uncommitted = aggregate.UncommittedEvents.ToList();
        if (uncommitted.Count == 0)
        {
            _logger.LogInformation("Temporal fact assertion was idempotent for user {UserId} ({Subject} {Predicate} {Object})",
                request.UserId, request.Subject, request.Predicate, request.Object);
            return Guid.NewGuid();
        }

        await _eventStore.AppendEventsAsync(streamId, uncommitted, expectedVersion, cancellationToken);
        await _eventBus.PublishAsync(uncommitted, cancellationToken);

        aggregate.ClearUncommittedEvents();

        var assertedEvt = uncommitted.OfType<TemporalFactAssertedEvent>().FirstOrDefault();
        var invalidatedEvt = uncommitted.OfType<TemporalFactInvalidatedEvent>().FirstOrDefault();

        if (invalidatedEvt != null)
        {
            _logger.LogInformation(
                "Temporal invalidation triggered: [{Subject}] {Predicate} [{OldObject}] was superseded by [{NewObject}] for user {UserId}",
                request.Subject, request.Predicate, invalidatedEvt.OldObject, request.Object, request.UserId);
        }

        return assertedEvt?.FactId ?? Guid.NewGuid();
    }
}
