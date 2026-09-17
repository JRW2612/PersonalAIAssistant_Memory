using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;
using MediatR;

namespace PersonalAIAssistant.Memory.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
public class AgentToolsController : ControllerBase
{
    private readonly IAgentToolService _toolService;
    private readonly IMediator _mediator;

    public AgentToolsController(IAgentToolService toolService, IMediator mediator)
    {
        _toolService = toolService;
        _mediator = mediator;
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
    /// Returns standardized tool schema definitions (Letta-style) ready for OpenAI/Gemini function calling.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentToolDefinition>), StatusCodes.Status200OK)]
    public IActionResult GetToolDefinitions()
    {
        var tools = _toolService.GetToolDefinitions();
        return Ok(tools);
    }

    /// <summary>
    /// Executes a tool call invoked by the LLM agent during its reasoning loop.
    /// </summary>
    [HttpPost("execute")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(typeof(AgentToolExecutionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExecuteTool([FromBody] AgentToolExecutionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            return BadRequest("ToolName must not be empty.");
        }

        var userId = GetCurrentUserId();
        var response = await _toolService.ExecuteToolAsync(userId, request, ct);
        return Ok(response);
    }

    /// <summary>
    /// Explicitly asserts an entity relationship fact with automatic temporal invalidation (Zep/Graphiti-style).
    /// </summary>
    [HttpPost("facts")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AssertFact([FromBody] AssertFactRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var command = new AssertTemporalFactCommand(
            Subject: request.Subject,
            Predicate: request.Predicate,
            Object: request.Object,
            UserId: userId,
            Confidence: request.Confidence,
            Source: request.Source ?? "Api");

        var factId = await _mediator.Send(command, ct);
        return Ok(new { factId, status = "asserted" });
    }

    /// <summary>
    /// Retrieves all currently valid temporal facts for the authenticated user, optionally filtered by subject.
    /// </summary>
    [HttpGet("facts")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(IReadOnlyList<TemporalFact>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentFacts([FromQuery] string? subject, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var query = new GetActiveFactsQuery(userId, subject);
        var facts = await _mediator.Send(query, ct);
        return Ok(facts);
    }
}
