using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Models;
using System.Collections.Generic;

namespace PersonalAIAssistant.Memory.Api.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class MemoriesController : ControllerBase
    {
        private readonly IMediator _mediator;

        public MemoriesController(IMediator mediator)
        {
            _mediator = mediator;
        }

        private string GetCurrentUserId()
        {
            if (HttpContext.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is string userId && !string.IsNullOrEmpty(userId))
            {
                return userId;
            }
            return User.Identity?.Name ?? "anonymous-user";
        }

        /// <summary>
        /// Ingests a new memory, splitting long text into overlapping chunks, generating vector embeddings, and storing event streams.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
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
            return CreatedAtAction(nameof(GetMemoryById), new { id = memoryId }, new { MemoryId = memoryId });
        }

        /// <summary>
        /// Queries and retrieves user memories using hybrid vector similarity search, recency decay, and importance scoring.
        /// </summary>
        [HttpGet("search")]
        [ProducesResponseType(typeof(FusedMemoryPrompt), StatusCodes.Status200OK)]
        public async Task<IActionResult> SearchMemories([FromQuery] string query, [FromQuery] int topK = 5, CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            var queryRequest = new RetrieveMemoriesQuery(
                UserId: userId,
                QueryText: query,
                TopK: topK <= 0 ? 5 : topK
            );

            var result = await _mediator.Send(queryRequest, ct);
            return Ok(result);
        }

        /// <summary>
        /// Fetches memory details by ID for the current authenticated user.
        /// </summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(RetrievedMemory), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMemoryById(Guid id, CancellationToken ct)
        {
            var userId = GetCurrentUserId();
            var queryRequest = new RetrieveMemoriesQuery(UserId: userId, QueryText: string.Empty, TopK: 100);
            var promptResult = await _mediator.Send(queryRequest, ct);
            
            var match = promptResult.Sources.FirstOrDefault(m => m.MemoryId == id);
            if (match == null) return NotFound($"Memory with ID '{id}' was not found.");
            
            return Ok(match);
        }

        /// <summary>
        /// Updates the text payload of an existing memory aggregate.
        /// </summary>
        [HttpPut("{id:guid}")]
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
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> CompressMemory(Guid id, [FromBody] CompressRequestDto dto, CancellationToken ct)
        {
            var userId = GetCurrentUserId();
            var command = new CompressMemoryCommand(id, dto.CompressedText, dto.Model ?? "gpt-4o-mini", dto.TokenCount, userId);
            await _mediator.Send(command, ct);
            return Ok(new { MemoryId = id, Status = "Compressed" });
        }

        /// <summary>
        /// Explicitly triggers consolidation across multiple memory candidates.
        /// </summary>
        [HttpPost("consolidate")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ConsolidateMemories([FromBody] ConsolidateRequestDto dto, CancellationToken ct = default)
        {
            var userId = GetCurrentUserId();
            var command = new ConsolidateMemoriesCommand(
                NewMemoryId: Guid.NewGuid(),
                MergedMemoryIds: dto.MergedMemoryIds,
                ConsolidatedText: dto.ConsolidatedText,
                UserId: userId,
                ProvenanceLinks: dto.ProvenanceLinks ?? new List<string>()
            );
            var newId = await _mediator.Send(command, ct);
            return Ok(new { NewMemoryId = newId, Status = "Consolidated" });
        }
    }

    public record CreateMemoryDto(
        string RawText,
        string? Source,
        MemoryImportance Importance,
        List<string>? Tags,
        string? CorrelationId
    );

    public record UpdateMemoryDto(
        string RawText
    );

    public record CompressRequestDto(
        string CompressedText,
        string? Model,
        int TokenCount
    );

    public record ConsolidateRequestDto(
        List<Guid> MergedMemoryIds,
        string ConsolidatedText,
        List<string>? ProvenanceLinks
    );
}
