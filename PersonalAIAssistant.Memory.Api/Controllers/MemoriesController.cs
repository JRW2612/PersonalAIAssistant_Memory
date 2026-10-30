using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PersonalAIAssistant.Memory.Api.DTOs;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
public class MemoriesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IEmbeddingService? _embeddingService;
    private readonly IVectorMemoryRepository? _vectorRepo;

    public MemoriesController(
        IMediator mediator,
        IEmbeddingService? embeddingService = null,
        IVectorMemoryRepository? vectorRepo = null)
    {
        _mediator = mediator;
        _embeddingService = embeddingService;
        _vectorRepo = vectorRepo;
    }

    private string GetCurrentUserId()
    {
        if (HttpContext?.Items.TryGetValue("UserId", out var userIdObj) == true && userIdObj is string userId && !string.IsNullOrEmpty(userId))
        {
            return userId;
        }

        var identityName = User?.Identity?.Name;
        if (!string.IsNullOrEmpty(identityName))
        {
            return identityName;
        }

        var sub = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
               ?? User?.FindFirst("sub")?.Value;

        if (!string.IsNullOrEmpty(sub))
        {
            return sub;
        }

        throw new UnauthorizedAccessException("Authenticated user identity could not be resolved from token claims.");
    }

    /// <summary>
    /// Ingests a new memory, splitting long text into overlapping chunks, generating vector embeddings, and storing event streams.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(typeof(CreateMemoryResponseDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddMemory([FromBody] CreateMemoryDto dto, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var command = new AddMemoryCommand(
            RawText: dto.RawText,
            Source: string.IsNullOrWhiteSpace(dto.Source) ? MemorySource.Api.ToString() : dto.Source,
            Importance: dto.Importance,
            Tags: dto.Tags ?? new List<string>(),
            UserId: userId,
            CorrelationId: dto.CorrelationId
        );

        var memoryId = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetMemoryById), new { id = memoryId }, new CreateMemoryResponseDto(memoryId));
    }

    /// <summary>
    /// Ingests a visual memory (camera snapshot, whiteboard drawing, diagram), generating visual vector embeddings
    /// and indexing into the vector store.
    /// </summary>
    [HttpPost("visual")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(typeof(CreateMemoryResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddVisualMemory([FromBody] CreateVisualMemoryDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ImageBase64))
        {
            return BadRequest("ImageBase64 payload must not be empty.");
        }

        var userId = GetCurrentUserId();
        var visualContent = VisualContent.FromBase64(
            dto.ImageBase64,
            dto.MimeType,
            dto.Description,
            dto.Modality);

        var description = string.IsNullOrWhiteSpace(dto.Description)
            ? $"[{dto.Modality}] Visual observation captured at {DateTime.UtcNow:u}"
            : $"[{dto.Modality}] {dto.Description}";

        var tags = dto.Tags ?? new List<string>();
        var modalityTag = dto.Modality.ToString().ToLowerInvariant();
        if (!tags.Contains(modalityTag))
        {
            tags.Add(modalityTag);
        }
        if (!tags.Contains("visual-memory"))
        {
            tags.Add("visual-memory");
        }

        var command = new AddMemoryCommand(
            RawText: description,
            Source: string.IsNullOrWhiteSpace(dto.Source) ? "VisualCapture" : dto.Source,
            Importance: dto.Importance,
            Tags: tags,
            UserId: userId,
            CorrelationId: dto.CorrelationId
        );

        var memoryId = await _mediator.Send(command, ct);

        // If visual embedding service & vector repository are injected, directly index the visual vector
        if (_embeddingService != null && _vectorRepo != null)
        {
            try
            {
                var embedding = await _embeddingService.GenerateVisualEmbeddingAsync(visualContent, ct);
                await _vectorRepo.UpsertAsync(memoryId, embedding.EmbeddingId, embedding.Vector, userId, ct);
                await _mediator.Send(new MemoryIndexedCommand(memoryId, embedding.EmbeddingId, embedding.Provider), ct);
            }
            catch
            {
                // Background worker fallback handles textual indexing
            }
        }

        return CreatedAtAction(nameof(GetMemoryById), new { id = memoryId }, new CreateMemoryResponseDto(memoryId));
    }

    /// <summary>
    /// Queries and retrieves user memories using hybrid vector similarity search, recency decay, and importance scoring.
    /// </summary>
    [HttpGet("search")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(FusedMemoryPrompt), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchMemories([FromQuery] string query, [FromQuery] int topK = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest("Query text must not be empty.");
        }

        if (query.Length > 1000)
        {
            return BadRequest("Query text must not exceed 1,000 characters.");
        }

        var userId = GetCurrentUserId();
        var safeTopK = Math.Clamp(topK <= 0 ? 5 : topK, 1, 50);
        var queryRequest = new RetrieveMemoriesQuery(
            UserId: userId,
            QueryText: query,
            TopK: safeTopK
        );

        var result = await _mediator.Send(queryRequest, ct);
        return Ok(result);
    }

    /// <summary>
    /// Searches user memories using visual inputs (camera snapshots, whiteboard drawings) alongside optional query text.
    /// </summary>
    [HttpPost("search-visual")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(FusedMemoryPrompt), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchVisualMemories([FromBody] SearchVisualMemoryDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.ImageBase64))
        {
            return BadRequest("ImageBase64 payload must not be empty.");
        }

        var userId = GetCurrentUserId();
        var visualQuery = VisualContent.FromBase64(
            dto.ImageBase64,
            dto.MimeType,
            dto.QueryText,
            dto.Modality);

        var safeTopK = Math.Clamp(dto.TopK <= 0 ? 5 : dto.TopK, 1, 50);
        var queryRequest = new RetrieveMemoriesQuery(
            UserId: userId,
            QueryText: dto.QueryText ?? string.Empty,
            TopK: safeTopK,
            VisualQuery: visualQuery
        );

        var result = await _mediator.Send(queryRequest, ct);
        return Ok(result);
    }

    /// <summary>
    /// Fetches memory details by ID for the current authenticated user.
    /// </summary>
    [HttpGet("{id:guid}")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(MemoryReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMemoryById(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var query = new GetMemoryByIdQuery(id, userId);
        var match = await _mediator.Send(query, ct);

        if (match == null) return NotFound($"Memory with ID '{id}' was not found.");

        return Ok(match);
    }

    /// <summary>
    /// Updates the text payload of an existing memory aggregate.
    /// </summary>
    [HttpPut("{id:guid}")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateMemory(Guid id, [FromBody] UpdateMemoryDto dto, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var updatedFields = new Dictionary<string, string>
        {
            { "RawText", dto.RawText }
        };

        var command = new UpdateMemoryCommand(id, userId, updatedFields);
        await _mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>
    /// Soft-deletes / archives a memory aggregate.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteMemory(Guid id, [FromQuery] string? reason, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var command = new DeleteMemoryCommand(id, reason ?? "User requested deletion", userId);
        await _mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>
    /// Explicitly triggers LLM compression on a specific memory item.
    /// </summary>
    [HttpPost("{id:guid}/compress")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(typeof(CompressMemoryResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CompressMemory(Guid id, [FromBody] CompressRequestDto dto, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var command = new CompressMemoryCommand(id, dto.CompressedText, dto.Model ?? "gpt-4o-mini", dto.TokenCount, userId);
        await _mediator.Send(command, ct);
        return Ok(new CompressMemoryResponseDto(id, "Compressed"));
    }

    /// <summary>
    /// Explicitly triggers consolidation across multiple memory candidates.
    /// </summary>
    [HttpPost("consolidate")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(typeof(ConsolidateMemoriesResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConsolidateMemories([FromBody] ConsolidateRequestDto dto, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var command = new ConsolidateMemoriesCommand(
            NewMemoryId: Guid.NewGuid(),
            MergedMemoryIds: dto.MergedMemoryIds,
            ConsolidatedText: dto.ConsolidatedText,
            UserId: userId,
            ProvenanceLinks: dto.ProvenanceLinks
        );

        var resultId = await _mediator.Send(command, ct);
        return Ok(new ConsolidateMemoriesResponseDto(resultId, "Consolidated"));
    }

    /// <summary>
    /// Places a legal hold on a memory, preventing deletion and automated retention purge.
    /// Requires ComplianceAuditor or Admin role.
    /// </summary>
    [HttpPost("{id:guid}/legal-hold")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [Authorize(Roles = "ComplianceAuditor,Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ApplyLegalHold(Guid id, [FromBody] LegalHoldDto dto, CancellationToken ct)
    {
        var auditorId = GetCurrentUserId();
        var command = new PersonalAIAssistant.Memory.Business.Commands.ApplyLegalHoldCommand(id, dto.Reason, auditorId);
        await _mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>
    /// Releases a legal hold from a memory, restoring normal lifecycle.
    /// Requires ComplianceAuditor or Admin role.
    /// </summary>
    [HttpDelete("{id:guid}/legal-hold")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [Authorize(Roles = "ComplianceAuditor,Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReleaseLegalHold(Guid id, CancellationToken ct)
    {
        var auditorId = GetCurrentUserId();
        var command = new PersonalAIAssistant.Memory.Business.Commands.ReleaseLegalHoldCommand(id, auditorId);
        await _mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>
    /// Purges all memories for a target user (GDPR Article 17 / employee offboarding).
    /// Requires ComplianceAuditor or Admin role.
    /// </summary>
    [HttpPost("users/{userId}/purge")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [Authorize(Roles = "ComplianceAuditor,Admin")]
    [ProducesResponseType(typeof(PurgeResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> PurgeUserMemories(string userId, [FromBody] PurgeRequestDto dto, CancellationToken ct)
    {
        var requestedBy = GetCurrentUserId();
        var command = new PersonalAIAssistant.Memory.Business.Commands.PurgeUserMemoriesCommand(userId, requestedBy, dto.Reason);
        var count = await _mediator.Send(command, ct);
        return Ok(new PurgeResponseDto(userId, count, "Purged"));
    }

    /// <summary>
    /// Records a new message turn (user or assistant) to the active session conversation buffer.
    /// </summary>
    [HttpPost("session/{sessionId}/turns")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AppendSessionTurn(string sessionId, [FromBody] AppendTurnDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest("SessionId must not be empty.");
        }

        var userId = GetCurrentUserId();
        var command = new AppendConversationTurnCommand(
            UserId: userId,
            SessionId: sessionId,
            Role: dto.Role,
            Content: dto.Content);

        await _mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>
    /// Builds a combined system prompt context containing the active rolling conversation turns
    /// and semantically relevant long-term memories retrieved for the current message.
    /// </summary>
    [HttpPost("session/{sessionId}/context")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(SessionContext), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSessionContext(string sessionId, [FromBody] GetSessionContextDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest("SessionId must not be empty.");
        }

        var userId = GetCurrentUserId();
        var safeTopK = Math.Clamp(dto.TopK <= 0 ? 5 : dto.TopK, 1, 50);
        var safeMaxTurns = Math.Clamp(dto.MaxBufferTurns <= 0 ? 10 : dto.MaxBufferTurns, 1, 100);

        var query = new BuildSessionContextQuery(
            UserId: userId,
            SessionId: sessionId,
            CurrentMessage: dto.CurrentMessage ?? string.Empty,
            TopK: safeTopK,
            MaxBufferTurns: safeMaxTurns);

        var context = await _mediator.Send(query, ct);
        return Ok(context);
    }
}
