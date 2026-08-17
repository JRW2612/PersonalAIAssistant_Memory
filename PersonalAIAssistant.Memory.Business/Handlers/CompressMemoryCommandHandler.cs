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
    /// Handles CompressMemoryCommand.
    /// Delegates compression to ICompressionService and persists aggregate updates.
    /// </summary>
    public class CompressMemoryCommandHandler : IRequestHandler<CompressMemoryCommand, Guid>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;
        private readonly ICompressionService _compressionService;
        private readonly ILogger<CompressMemoryCommandHandler> _logger;

        public CompressMemoryCommandHandler(
            IEventStore eventStore,
            IEventBus eventBus,
            ICompressionService compressionService,
            ILogger<CompressMemoryCommandHandler> logger)
        {
            _eventStore         = eventStore;
            _eventBus           = eventBus;
            _compressionService = compressionService;
            _logger             = logger;
        }

        public async Task<Guid> Handle(CompressMemoryCommand request, CancellationToken cancellationToken)
        {
            // ── 1. Load aggregate ────────────────────────────────────────────────
            var streamId = $"memory-{request.OriginalMemoryId}";

            var eventHistory = await _eventStore.GetEventsAsync(streamId, cancellationToken);
            if (eventHistory == null || !eventHistory.Any())
                throw new KeyNotFoundException($"No events found for memory with ID {streamId}");

            var aggregate = new MemoryAggregate(new MemoryId(request.OriginalMemoryId));
            aggregate.LoadFromHistory(eventHistory);

            // ── 2. Determine compressed text ─────────────────────────────────────
            var compressedText   = request.CompressedText;
            var compressionModel = request.CompressionModel;
            var tokenCount       = request.TokenCount;

            if (string.IsNullOrWhiteSpace(compressedText))
            {
                var textToCompress = aggregate.CompressedText ?? aggregate.RawText;
                var result = await _compressionService.CompressAsync(textToCompress, cancellationToken);
                compressedText   = result.Text;
                compressionModel = result.Model;
                tokenCount       = result.TokenCount;
            }

            // ── 3. Apply and persist ─────────────────────────────────────────────
            aggregate.Compress(compressedText, compressionModel, tokenCount, request.UserId);

            var uncommittedEvents = aggregate.UncommittedEvents.ToList();
            if (!uncommittedEvents.Any())
                return aggregate.Id.Value;

            var expectedVersion = aggregate.Version - uncommittedEvents.Count;
            await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, expectedVersion, cancellationToken);
            await _eventBus.PublishAsync(uncommittedEvents, cancellationToken);
            aggregate.ClearUncommittedEvents();

            return aggregate.Id.Value;
        }
    }
}
