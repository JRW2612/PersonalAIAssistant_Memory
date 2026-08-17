using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    /// <summary>
    /// Handles ConsolidateMemoriesCommand.
    /// Uses an AI model (via IAIProviderFactory) to produce a coherent,
    /// de-duplicated summary and persists the consolidated aggregate.
    /// Notifications are decoupled and dispatched via event handling (MemoryConsolidatedNotificationHandler).
    /// </summary>
    public class ConsolidateMemoriesCommandHandler : IRequestHandler<ConsolidateMemoriesCommand, Guid>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;
        private readonly IAIProviderFactory _aiFactory;
        private readonly ILogger<ConsolidateMemoriesCommandHandler> _logger;

        public ConsolidateMemoriesCommandHandler(
            IEventStore eventStore,
            IEventBus eventBus,
            IAIProviderFactory aiFactory,
            ILogger<ConsolidateMemoriesCommandHandler> logger)
        {
            _eventStore = eventStore;
            _eventBus   = eventBus;
            _aiFactory  = aiFactory;
            _logger     = logger;
        }

        public async Task<Guid> Handle(ConsolidateMemoriesCommand request, CancellationToken cancellationToken)
        {
            // ── 1. AI-assisted semantic consolidation ────────────────────────────
            var enrichedText = request.ConsolidatedText;
            try
            {
                var provider = _aiFactory.GetProvider();
                var prompt =
                    $"""
                    You are a personal memory assistant.
                    Merge the following memory fragments into one coherent, de-duplicated summary.
                    Preserve all factual details. Be concise — max 3 sentences.

                    Memory fragments:
                    {request.ConsolidatedText}
                    """;

                enrichedText = await provider.GetResponseAsync(prompt, cancellationToken);
                _logger.LogInformation(
                    "[Consolidate] AI enrichment complete — provider: {Provider}, memoryId: {Id}",
                    provider.ProviderName, request.NewMemoryId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[Consolidate] AI enrichment failed for memoryId {Id} — using original text.",
                    request.NewMemoryId);
            }

            // ── 2. Build and persist the aggregate ───────────────────────────────
            var newId     = request.NewMemoryId != Guid.Empty ? request.NewMemoryId : Guid.NewGuid();
            var aggregate = new MemoryAggregate(new MemoryId(newId));

            aggregate.Consolidate(
                enrichedText,
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

            await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, 0, cancellationToken);
            await _eventBus.PublishAsync(uncommittedEvents, cancellationToken);
            aggregate.ClearUncommittedEvents();

            return aggregate.Id.Value;
        }
    }
}
