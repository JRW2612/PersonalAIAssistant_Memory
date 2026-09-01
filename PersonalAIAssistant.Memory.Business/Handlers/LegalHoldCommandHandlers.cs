using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    public sealed class ApplyLegalHoldCommandHandler : IRequestHandler<ApplyLegalHoldCommand>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;
        private readonly ILogger<ApplyLegalHoldCommandHandler> _logger;

        public ApplyLegalHoldCommandHandler(IEventStore eventStore, IEventBus eventBus, ILogger<ApplyLegalHoldCommandHandler> logger)
        {
            _eventStore = eventStore;
            _eventBus = eventBus;
            _logger = logger;
        }

        public async Task Handle(ApplyLegalHoldCommand request, CancellationToken cancellationToken)
        {
            var streamId = $"memory-{request.MemoryId}";
            var history = await _eventStore.GetEventsAsync(streamId, cancellationToken);
            if (!history.Any()) throw new KeyNotFoundException($"Memory stream '{streamId}' not found.");

            var aggregate = new MemoryAggregate();
            aggregate.LoadFromHistory(history);
            aggregate.ApplyLegalHold(request.Reason, request.AuditorId);

            var newEvents = aggregate.UncommittedEvents.ToList();
            if (newEvents.Any())
            {
                var expectedVersion = aggregate.Version - newEvents.Count;
                await _eventStore.AppendEventsAsync(streamId, newEvents, expectedVersion, cancellationToken);
                await _eventBus.PublishAsync(newEvents, cancellationToken);
            }

            _logger.LogInformation("[Legal Hold] Applied to memory {MemoryId} by auditor {AuditorId}, reason: {Reason}",
                request.MemoryId, request.AuditorId, request.Reason);
        }
    }

    public sealed class ReleaseLegalHoldCommandHandler : IRequestHandler<ReleaseLegalHoldCommand>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;
        private readonly ILogger<ReleaseLegalHoldCommandHandler> _logger;

        public ReleaseLegalHoldCommandHandler(IEventStore eventStore, IEventBus eventBus, ILogger<ReleaseLegalHoldCommandHandler> logger)
        {
            _eventStore = eventStore;
            _eventBus = eventBus;
            _logger = logger;
        }

        public async Task Handle(ReleaseLegalHoldCommand request, CancellationToken cancellationToken)
        {
            var streamId = $"memory-{request.MemoryId}";
            var history = await _eventStore.GetEventsAsync(streamId, cancellationToken);
            if (!history.Any()) throw new KeyNotFoundException($"Memory stream '{streamId}' not found.");

            var aggregate = new MemoryAggregate();
            aggregate.LoadFromHistory(history);
            aggregate.ReleaseLegalHold(request.AuditorId);

            var newEvents = aggregate.UncommittedEvents.ToList();
            if (newEvents.Any())
            {
                var expectedVersion = aggregate.Version - newEvents.Count;
                await _eventStore.AppendEventsAsync(streamId, newEvents, expectedVersion, cancellationToken);
                await _eventBus.PublishAsync(newEvents, cancellationToken);
            }

            _logger.LogInformation("[Legal Hold] Released from memory {MemoryId} by auditor {AuditorId}",
                request.MemoryId, request.AuditorId);
        }
    }
}
