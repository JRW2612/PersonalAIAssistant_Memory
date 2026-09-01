using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PersonalAIAssistant.Memory.Api.DTOs;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Api.Controllers
{
    [Authorize]
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
            if (HttpContext?.Items.TryGetValue("UserId", out var userIdObj) == true && userIdObj is string userId && !string.IsNullOrEmpty(userId))
            {
                return userId;
            }
            return User?.Identity?.Name ?? "anonymous-user";
        }

        /// <summary>
        /// Ingests a new memory, splitting long text into overlapping chunks, generating vector embeddings, and storing event streams.
        /// </summary>
        [HttpPost]
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
        [ProducesResponseType(typeof(ConsolidateMemoriesResponseDto), StatusCodes.Status200OK)]
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
            return Ok(new ConsolidateMemoriesResponseDto(newId, "Consolidated"));
        }

        /// <summary>
        /// Applies a legal hold to a memory, preventing automated deletion and archival.
        /// Requires ComplianceAuditor or Admin role.
        /// </summary>
        [HttpPost("{id:guid}/legal-hold")]
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
        [Authorize(Roles = "ComplianceAuditor,Admin")]
        [ProducesResponseType(typeof(PurgeResponseDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> PurgeUserMemories(string userId, [FromBody] PurgeRequestDto dto, CancellationToken ct)
        {
            var requestedBy = GetCurrentUserId();
            var command = new PersonalAIAssistant.Memory.Business.Commands.PurgeUserMemoriesCommand(userId, requestedBy, dto.Reason);
            var count = await _mediator.Send(command, ct);
            return Ok(new PurgeResponseDto(userId, count, "Purged"));
        }
    }
}
