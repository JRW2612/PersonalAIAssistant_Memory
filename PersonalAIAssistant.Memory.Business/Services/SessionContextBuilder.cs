// PersonalAIAssistant.Memory.Business/Services/SessionContextBuilder.cs
using System.Text;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Services;

/// <summary>
/// Fuses short-term conversation buffer turns and long-term vector-retrieved memories
/// into a structured system prompt fragment for LLMs, with automated condensation
/// and promotion when buffers hit the configured threshold.
/// </summary>
public class SessionContextBuilder : ISessionContextBuilder
{
    private readonly IConversationBuffer _buffer;
    private readonly IMemoryRetrievalService _retrievalService;
    private readonly ICompressionService _compressionService;
    private readonly IMediator _mediator;
    private readonly PersonalAIAssistant.Memory.Core.Interfaces.Persistence.ITemporalFactRepository _temporalFactRepo;
    private readonly ConversationBufferOptions _options;
    private readonly ILogger<SessionContextBuilder> _logger;

    public SessionContextBuilder(
        IConversationBuffer buffer,
        IMemoryRetrievalService retrievalService,
        ICompressionService compressionService,
        IMediator mediator,
        PersonalAIAssistant.Memory.Core.Interfaces.Persistence.ITemporalFactRepository temporalFactRepo,
        IOptions<ConversationBufferOptions> options,
        ILogger<SessionContextBuilder> logger)
    {
        _buffer = buffer;
        _retrievalService = retrievalService;
        _compressionService = compressionService;
        _mediator = mediator;
        _temporalFactRepo = temporalFactRepo;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SessionContext> BuildAsync(SessionContextRequest request, CancellationToken ct = default)
    {
        // 1. Fetch recent short-term conversation turns
        var recentTurns = await _buffer.GetRecentTurnsAsync(
            request.UserId,
            request.SessionId,
            request.MaxBufferTurns,
            ct);

        // 2. Fetch semantically relevant long-term memories
        IReadOnlyList<RetrievedMemory> longTermMemories = Array.Empty<RetrievedMemory>();
        if (!string.IsNullOrWhiteSpace(request.CurrentMessage))
        {
            var retrievalRequest = new RetrievalRequest(
                UserId: request.UserId,
                QueryText: request.CurrentMessage,
                TopK: request.TopK);

            try
            {
                longTermMemories = await _retrievalService.RetrieveAsync(retrievalRequest, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve long-term memories for user {UserId}. Continuing with empty memory set.", request.UserId);
            }
        }

        // 3. Fetch active verified temporal facts
        IReadOnlyList<TemporalFact> temporalFacts = Array.Empty<TemporalFact>();
        try
        {
            temporalFacts = await _temporalFactRepo.GetCurrentFactsAsync(request.UserId, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve temporal facts for user {UserId}. Continuing.", request.UserId);
        }

        // 4. Automated compression & promotion if buffer exceeded flush threshold
        if (_options.AutoFlushToMemory)
        {
            await TryFlushBufferAsync(request.UserId, request.SessionId, ct);
        }

        // 5. Assemble system prompt fragment
        var fragment = FormatSystemPromptFragment(recentTurns, longTermMemories, temporalFacts);

        return new SessionContext(fragment, recentTurns, longTermMemories);
    }

    private async Task TryFlushBufferAsync(string userId, string sessionId, CancellationToken ct)
    {
        try
        {
            var currentCount = await _buffer.GetTurnCountAsync(userId, sessionId, ct);
            if (currentCount < _options.FlushThreshold)
            {
                return;
            }

            _logger.LogInformation(
                "Conversation buffer for user {UserId} session {SessionId} reached threshold ({Count}/{Threshold}). Flushing to long-term memory.",
                userId, sessionId, currentCount, _options.FlushThreshold);

            var flushedTurns = await _buffer.FlushAndClearAsync(userId, sessionId, ct);
            if (flushedTurns.Count == 0) return;

            // Condense / summarize flushed dialogue
            var transcriptBuilder = new StringBuilder();
            foreach (var turn in flushedTurns)
            {
                transcriptBuilder.AppendLine($"{turn.Timestamp:yyyy-MM-dd HH:mm:ss} [{turn.Role}]: {turn.Content}");
            }

            var compressed = await _compressionService.CompressAsync(transcriptBuilder.ToString(), ct);

            // Promote compressed dialogue segment to long-term event store
            var addCommand = new AddMemoryCommand(
                RawText: compressed.Text,
                Source: "ConversationBuffer",
                Tags: new[] { "source:ConversationBuffer", $"session:{sessionId}", "promoted-dialogue" },
                UserId: userId,
                Importance: MemoryImportance.Low,
                CorrelationId: Guid.NewGuid().ToString());

            await _mediator.Send(addCommand, ct);

            _logger.LogInformation("Promoted compressed dialogue ({TokenCount} tokens) to long-term memory for user {UserId}.",
                compressed.TokenCount, userId);
        }
        catch (Exception ex)
        {
            // Do not block the primary prompt assembly flow if background auto-flush promotion encounters an issue
            _logger.LogError(ex, "Error during automated buffer flush promotion for user {UserId}, session {SessionId}.",
                userId, sessionId);
        }
    }

    private static string FormatSystemPromptFragment(
        IReadOnlyList<ConversationTurn> recentTurns,
        IReadOnlyList<RetrievedMemory> longTermMemories,
        IReadOnlyList<TemporalFact> temporalFacts)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Context & Memory Profile");
        sb.AppendLine();

        // Section A: Current verified temporal facts
        sb.AppendLine("## Current Verified Facts (Temporally Grounded)");
        if (temporalFacts.Count > 0)
        {
            foreach (var fact in temporalFacts)
            {
                sb.AppendLine($"- [{fact.Subject}] {fact.Predicate} [{fact.Object}] (valid since: {fact.ValidFromUtc:yyyy-MM-dd})");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("No specific verified entity facts recorded yet.");
            sb.AppendLine();
        }

        // Section B: Long-term retrieved knowledge
        sb.AppendLine("## Relevant Long-Term Memories");
        if (longTermMemories.Count > 0)
        {
            for (int i = 0; i < longTermMemories.Count; i++)
            {
                var mem = longTermMemories[i];
                sb.AppendLine($"[{i + 1}] (Importance: {mem.Importance}, Date: {mem.CreatedAt:yyyy-MM-dd}, Score: {mem.Score:F2})");
                sb.AppendLine(mem.Text);
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("No relevant long-term memories found for this query.");
            sb.AppendLine();
        }

        // Section B: Short-term rolling buffer
        sb.AppendLine("## Recent Conversation History");
        if (recentTurns.Count > 0)
        {
            foreach (var turn in recentTurns)
            {
                sb.AppendLine($"[{turn.Timestamp:yyyy-MM-dd HH:mm:ss}] {turn.Role.ToUpperInvariant()}: {turn.Content}");
            }
        }
        else
        {
            sb.AppendLine("No prior turns in this session buffer.");
        }

        sb.AppendLine();
        sb.AppendLine("Instruction: Seamlessly integrate relevant long-term facts and recent conversation turns when formulating your response.");

        return sb.ToString().TrimEnd();
    }
}
