using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Interfaces.Mongo;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    /// <summary>
    /// Handles CompressMemoryCommand.
    /// Supports two compression modes controlled by AiProviderOptions.Enabled:
    ///   • AI mode   — calls a smaller/cheaper model to produce a semantic summary.
    ///   • Local mode — falls back to the injected ICompressionService (deterministic/rule-based).
    /// AI call failures always fall back to local compression; the handler never fails due to AI.
    /// </summary>
    public class CompressMemoryCommandHandler : IRequestHandler<CompressMemoryCommand, Guid>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;
        private readonly IAIProviderFactory _aiFactory;
        private readonly ICompressionService _localCompressor;
        private readonly ILogger<CompressMemoryCommandHandler> _logger;

        public CompressMemoryCommandHandler(
            IEventStore eventStore,
            IEventBus eventBus,
            IAIProviderFactory aiFactory,
            ICompressionService localCompressor,
            ILogger<CompressMemoryCommandHandler> logger)
        {
            _eventStore      = eventStore;
            _eventBus        = eventBus;
            _aiFactory       = aiFactory;
            _localCompressor = localCompressor;
            _logger          = logger;
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
            // If the command already carries pre-compressed text (from a worker), use it.
            // Otherwise attempt AI semantic compression, fall back to local if AI fails.
            var compressedText  = request.CompressedText;
            var compressionModel = request.CompressionModel;

            if (string.IsNullOrWhiteSpace(compressedText))
            {
                (compressedText, compressionModel) = await TryAiCompressAsync(aggregate, cancellationToken)
                    ?? await LocalCompressAsync(aggregate, cancellationToken);
            }

            // ── 3. Apply and persist ─────────────────────────────────────────────
            aggregate.Compress(compressedText, compressionModel, request.TokenCount, request.UserId);

            var uncommittedEvents = aggregate.UncommittedEvents.ToList();
            if (!uncommittedEvents.Any())
                return aggregate.Id.Value;

            var expectedVersion = aggregate.Version - uncommittedEvents.Count;
            await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, expectedVersion, cancellationToken);
            await _eventBus.PublishAsync(uncommittedEvents, cancellationToken);
            aggregate.ClearUncommittedEvents();

            return aggregate.Id.Value;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Attempts AI-based semantic compression using the configured small/cheap model.
        /// Returns null on any failure so the caller can fall back to local compression.
        /// </summary>
        private async Task<(string Text, string Model)?> TryAiCompressAsync(
            MemoryAggregate aggregate,
            CancellationToken ct)
        {
            try
            {
                var provider   = _aiFactory.GetProvider(); // uses default; swap to "gemini" for cheaper
                var modelName  = $"{provider.ProviderName}-compress";

                // Use the most current text: prefer already-compressed text, fall back to raw.
                var textToCompress = aggregate.CompressedText ?? aggregate.RawText;

                var prompt =
                    $"""
                    Compress the following memory into one concise sentence that preserves its key facts.
                    Do not add commentary. Output only the compressed memory.

                    Memory:
                    {textToCompress}
                    """;

                var result = await provider.GetResponseAsync(prompt, ct);

                _logger.LogInformation(
                    "[Compress] AI compression complete — provider: {Provider}, memoryId: {Id}",
                    provider.ProviderName, aggregate.Id.Value);

                return (result, modelName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[Compress] AI compression failed for memoryId {Id} — falling back to local.",
                    aggregate.Id.Value);
                return null;
            }
        }

        /// <summary>
        /// Falls back to the deterministic ICompressionService (e.g., token trimming).
        /// </summary>
        private async Task<(string Text, string Model)> LocalCompressAsync(
            MemoryAggregate aggregate,
            CancellationToken ct)
        {
            var textToCompress = aggregate.CompressedText ?? aggregate.RawText;
            var result = await _localCompressor.CompressAsync(textToCompress, ct);
            return (result.Text, result.Model);
        }
    }
}
