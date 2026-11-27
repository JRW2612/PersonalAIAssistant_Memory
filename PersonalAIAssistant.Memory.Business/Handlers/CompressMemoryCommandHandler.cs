using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;

namespace PersonalAIAssistant.Memory.Business.Handlers;

/// <summary>
/// Handles CompressMemoryCommand.
/// Delegates compression to ICompressionService and persists aggregate updates.
/// </summary>
public class CompressMemoryCommandHandler : IRequestHandler<CompressMemoryCommand, Guid>
{
    private readonly IEventStore _eventStore;
    private readonly ICompressionService _compressionService;
    private readonly ILogger<CompressMemoryCommandHandler> _logger;

    public CompressMemoryCommandHandler(
        IEventStore eventStore,
        ICompressionService compressionService,
        ILogger<CompressMemoryCommandHandler> logger)
    {
        _eventStore = eventStore;
        _compressionService = compressionService;
        _logger = logger;
    }

    public async Task<Guid> Handle(CompressMemoryCommand request, CancellationToken cancellationToken)
    {
        // ── 1. Load aggregate ────────────────────────────────────────────────
        var streamId = $"memory-{request.OriginalMemoryId}";

        var eventHistory = await _eventStore.GetEventsAsync(streamId, cancellationToken);
        if (eventHistory == null || !eventHistory.Any())
            throw new KeyNotFoundException($"No events found for memory with ID {streamId}");

        // BOLA / IDOR Defense: verify memory ownership
        var ownerUserId = eventHistory.Select(e => e.UserId).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        if (!string.IsNullOrWhiteSpace(ownerUserId) &&
            !string.IsNullOrWhiteSpace(request.UserId) &&
            !string.Equals(request.UserId, "system", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ownerUserId, request.UserId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Memory '{request.OriginalMemoryId}' does not belong to user '{request.UserId}'.");
        }

        var aggregate = new MemoryAggregate(new MemoryId(request.OriginalMemoryId));
        aggregate.LoadFromHistory(eventHistory);

        // ── 2. Determine compressed text ─────────────────────────────────────
        var compressedText = request.CompressedText;
        var compressionModel = request.CompressionModel;
        var tokenCount = request.TokenCount;

        if (string.IsNullOrWhiteSpace(compressedText))
        {
            var textToCompress = aggregate.CompressedText ?? aggregate.RawText;
            var result = await _compressionService.CompressAsync(textToCompress, cancellationToken);
            compressedText = result.Text;
            compressionModel = result.Model;
            tokenCount = result.TokenCount;
        }

        // ── 3. Apply and persist ─────────────────────────────────────────────
        aggregate.Compress(compressedText, compressionModel, tokenCount, request.UserId);

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
