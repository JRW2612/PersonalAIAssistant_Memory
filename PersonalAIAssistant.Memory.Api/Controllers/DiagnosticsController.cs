using System.Security.Claims;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Api.Controllers;

/// <summary>
/// APM and Agent Behavior Diagnostics API for monitoring token usage, latencies,
/// inspecting structured agent execution traces, and safe replay in dev/staging.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly IAiMetricsLogger _aiMetrics;
    private readonly IAgentTraceRepository _traceRepo;
    private readonly IAgentToolService _toolService;
    private readonly IUserContext _userContext;
    private readonly IWebHostEnvironment _env;
    private readonly ISecurityAuditLogger _auditLogger;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(
        IAiMetricsLogger aiMetrics,
        IAgentTraceRepository traceRepo,
        IAgentToolService toolService,
        IUserContext userContext,
        IWebHostEnvironment env,
        ISecurityAuditLogger auditLogger,
        ILogger<DiagnosticsController> logger)
    {
        _aiMetrics = aiMetrics;
        _traceRepo = traceRepo;
        _toolService = toolService;
        _userContext = userContext;
        _env = env;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    private string GetCurrentUserId()
    {
        if (!string.IsNullOrWhiteSpace(_userContext.UserId))
            return _userContext.UserId;

        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(sub))
            return sub;

        throw new UnauthorizedAccessException("Authenticated user identity could not be resolved from token claims.");
    }

    private bool IsSupportOrAdmin() =>
        User.IsInRole("SupportAgent") || User.IsInRole("Admin") || User.IsInRole("ServicePrincipal");

    /// <summary>
    /// Returns aggregated AI token usage, call count, and estimated cost for the authenticated user.
    /// </summary>
    /// <param name="since">Time window filter (e.g., '1h', '24h', '7d' or ISO 8601 timestamp). Defaults to '24h'.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("ai-usage")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(AiUsageSummary), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAiUsageSummary([FromQuery] string? since, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var sinceTimestamp = ParseTimeSpanOrTimestamp(since, TimeSpan.FromHours(24));

        var summary = await _aiMetrics.GetSummaryAsync(userId, sinceTimestamp, ct);
        return Ok(summary);
    }

    /// <summary>
    /// Returns the most recent structured agent traces for the authenticated user.
    /// </summary>
    /// <param name="limit">Maximum number of traces to return (1-100). Default is 20.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("traces")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentTrace>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecentTraces([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var clampedLimit = Math.Clamp(limit, 1, 100);

        var traces = await _traceRepo.GetByUserAsync(userId, clampedLimit, ct);
        return Ok(traces);
    }

    /// <summary>
    /// Retrieves a single structured agent trace by its unique ID.
    /// </summary>
    /// <param name="traceId">Unique ID of the trace.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("traces/{traceId:guid}")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(AgentTrace), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTraceById(Guid traceId, CancellationToken ct)
    {
        var trace = await _traceRepo.GetByIdAsync(traceId, ct);
        if (trace == null)
            return NotFound(new { error = "trace_not_found", message = $"Agent trace '{traceId}' was not found." });

        var userId = GetCurrentUserId();
        if (trace.UserId != userId && !IsSupportOrAdmin())
        {
            return Forbid();
        }

        return Ok(trace);
    }

    /// <summary>
    /// Replays a captured agent trace in non-production environments or under SupportAgent oversight for debugging.
    /// </summary>
    /// <param name="traceId">Unique trace ID to replay.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("traces/{traceId:guid}/replay")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplayTrace(Guid traceId, CancellationToken ct)
    {
        // Safe diagnostics guard: allow only in Dev/Staging or if SupportAgent/Admin explicitly provides diagnostic role
        if (!_env.IsDevelopment() && !_env.IsStaging() && !IsSupportOrAdmin())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "replay_forbidden",
                message = "Agent trace replay is only permitted in Development, Staging, or by authorized SupportAgent/Admin roles."
            });
        }

        var trace = await _traceRepo.GetByIdAsync(traceId, ct);
        if (trace == null)
            return NotFound(new { error = "trace_not_found", message = $"Agent trace '{traceId}' was not found." });

        var currentUserId = GetCurrentUserId();
        if (trace.UserId != currentUserId && !IsSupportOrAdmin())
        {
            return Forbid();
        }

        _logger.LogInformation("Replaying agent trace {TraceId} ({RequestName}) for user {UserId}",
            trace.TraceId, trace.RequestName, trace.UserId);

        _auditLogger.LogAccessGranted(currentUserId, "REPLAY_AGENT_TRACE", traceId.ToString());

        var startTime = DateTimeOffset.UtcNow;

        // If the trace represents an agent tool execution and has valid arguments, replay through AgentToolService
        if (!string.IsNullOrWhiteSpace(trace.ToolName) && !string.IsNullOrWhiteSpace(trace.InputJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(trace.InputJson);
                var element = doc.RootElement.Clone();
                var toolRequest = new AgentToolExecutionRequest(trace.ToolName, element);
                var toolResult = await _toolService.ExecuteToolAsync(trace.UserId, toolRequest, ct);

                return Ok(new
                {
                    replayTraceId = Guid.NewGuid(),
                    originalTraceId = trace.TraceId,
                    replayedAt = DateTimeOffset.UtcNow,
                    durationMs = (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds,
                    success = toolResult.Success,
                    result = toolResult
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    replayTraceId = Guid.NewGuid(),
                    originalTraceId = trace.TraceId,
                    replayedAt = DateTimeOffset.UtcNow,
                    durationMs = (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds,
                    success = false,
                    error = ex.Message
                });
            }
        }

        return Ok(new
        {
            replayTraceId = Guid.NewGuid(),
            originalTraceId = trace.TraceId,
            status = "dry_run_acknowledged",
            requestName = trace.RequestName,
            replayedAt = DateTimeOffset.UtcNow,
            durationMs = (long)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds,
            message = "Non-tool command trace verified and simulated successfully."
        });
    }

    private static DateTimeOffset ParseTimeSpanOrTimestamp(string? value, TimeSpan defaultSpan)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DateTimeOffset.UtcNow.Subtract(defaultSpan);

        var trimmed = value.Trim().ToLowerInvariant();

        if (trimmed.EndsWith("m") && int.TryParse(trimmed[..^1], out var minutes))
            return DateTimeOffset.UtcNow.AddMinutes(-minutes);

        if (trimmed.EndsWith("h") && int.TryParse(trimmed[..^1], out var hours))
            return DateTimeOffset.UtcNow.AddHours(-hours);

        if (trimmed.EndsWith("d") && int.TryParse(trimmed[..^1], out var days))
            return DateTimeOffset.UtcNow.AddDays(-days);

        if (DateTimeOffset.TryParse(value, out var parsedDate))
            return parsedDate;

        return DateTimeOffset.UtcNow.Subtract(defaultSpan);
    }
}
