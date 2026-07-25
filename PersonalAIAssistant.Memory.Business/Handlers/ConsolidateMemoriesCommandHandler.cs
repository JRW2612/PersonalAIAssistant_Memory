using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Interfaces.Mongo;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    public class ConsolidateMemoriesCommandHandler : IRequestHandler<ConsolidateMemoriesCommand, Guid>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;
        private readonly ILogger<ConsolidateMemoriesCommandHandler> _logger;

        public ConsolidateMemoriesCommandHandler(
            IEventStore eventStore,
            IEventBus eventBus,
            ILogger<ConsolidateMemoriesCommandHandler> logger)
        {
            _eventStore = eventStore;
            _eventBus = eventBus;
            _logger = logger;
        }

        public async Task<Guid> Handle(ConsolidateMemoriesCommand request, CancellationToken cancellationToken)
        {
            // Use the provided ID or generate a fresh one for this consolidated stream.
            var newId = request.NewMemoryId != Guid.Empty ? request.NewMemoryId : Guid.NewGuid();
            var aggregate = new MemoryAggregate(new MemoryId(newId));

            aggregate.Consolidate(
                request.ConsolidatedText,
                request.MergedMemoryIds,
                request.ProvenanceLinks,
                request.UserId);

            var uncommittedEvents = aggregate.UncommittedEvents.ToList();
            if (!uncommittedEvents.Any())
            {
                _logger.LogWarning("ConsolidateMemories produced no events for new id {MemoryId}", newId);
                return newId;
            }

            var streamId = $"memory-{aggregate.Id.Value}";

            // New stream — expectedVersion is 0
            await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, 0, cancellationToken);
            await _eventBus.PublishAsync(uncommittedEvents, cancellationToken);
            aggregate.ClearUncommittedEvents();

            return aggregate.Id.Value;
        }
    }
}
