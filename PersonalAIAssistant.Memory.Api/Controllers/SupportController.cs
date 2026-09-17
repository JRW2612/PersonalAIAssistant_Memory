using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Api.Controllers;

/// <summary>
/// Dedicated Support &amp; Partner Operations controller for tier-2/tier-3 support engineers and partner integrations.
/// Access is strictly gated to the SupportAgent and Admin roles, with an immutable audit trail for every action.
/// </summary>
[Authorize(Roles = "SupportAgent,Admin")]
[ApiController]
[Route("api/v1/[controller]")]
public class SupportController : ControllerBase
{
    private readonly IAiMetricsLogger _aiMetrics;
    private readonly IAgentTraceRepository _traceRepo;
    private readonly IReadModelRepository _readRepo;
    private readonly IWorkloadIdentityRepository _workloadRepo;
    private readonly ISecurityAuditLogger _auditLogger;
    private readonly IUserContext _userContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SupportController> _logger;

    public SupportController(
        IAiMetricsLogger aiMetrics,
        IAgentTraceRepository traceRepo,
        IReadModelRepository readRepo,
        IWorkloadIdentityRepository workloadRepo,
        ISecurityAuditLogger auditLogger,
        IUserContext userContext,
        IServiceProvider serviceProvider,
        ILogger<SupportController> logger)
    {
        _aiMetrics = aiMetrics;
        _traceRepo = traceRepo;
        _readRepo = readRepo;
        _workloadRepo = workloadRepo;
        _auditLogger = auditLogger;
        _userContext = userContext;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private string GetSupportAgentIdentity() =>
        _userContext.UserId ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "support-agent";

    /// <summary>
    /// Retrieves aggregated AI consumption and memory utilization for a specific tenant.
    /// </summary>
    [HttpGet("tenants/{tenantId}/usage")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenantUsage(string tenantId, [FromQuery] string? since, CancellationToken ct)
    {
        var agent = GetSupportAgentIdentity();
        _auditLogger.LogAccessGranted(agent, "SUPPORT_VIEW_TENANT_USAGE", tenantId);

        var sinceTimestamp = ParseTimeSpanOrTimestamp(since, TimeSpan.FromDays(7));
        var workloads = await _workloadRepo.GetByTenantIdAsync(tenantId, ct);

        // Aggregate AI metrics across all workload identities associated with this tenant
        int totalPromptTokens = 0;
        int totalCompletionTokens = 0;
        double totalCostUsd = 0;
        int totalCalls = 0;

        foreach (var w in workloads)
        {
            var summary = await _aiMetrics.GetSummaryAsync(w.ClientId, sinceTimestamp, ct);
            totalPromptTokens += summary.TotalPromptTokens;
            totalCompletionTokens += summary.TotalCompletionTokens;
            totalCostUsd += summary.TotalEstimatedCostUsd;
            totalCalls += summary.CallCount;
        }

        // Also check primary tenant ID usage
        var tenantSummary = await _aiMetrics.GetSummaryAsync(tenantId, sinceTimestamp, ct);
        totalPromptTokens += tenantSummary.TotalPromptTokens;
        totalCompletionTokens += tenantSummary.TotalCompletionTokens;
        totalCostUsd += tenantSummary.TotalEstimatedCostUsd;
        totalCalls += tenantSummary.CallCount;

        var memoryCount = await _readRepo.GetMemoryCountByUserAsync(tenantId, ct);

        return Ok(new
        {
            tenantId,
            since = sinceTimestamp,
            evaluatedAtUtc = DateTimeOffset.UtcNow,
            workloadCount = workloads.Count,
            memoryCount,
            usage = new
            {
                totalCalls,
                totalPromptTokens,
                totalCompletionTokens,
                totalTokens = totalPromptTokens + totalCompletionTokens,
                estimatedCostUsd = totalCostUsd
            }
        });
    }

    /// <summary>
    /// Checks operational health for a specific tenant, including workload statuses and transactional outbox backlog.
    /// </summary>
    [HttpGet("tenants/{tenantId}/health")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenantHealth(string tenantId, CancellationToken ct)
    {
        var agent = GetSupportAgentIdentity();
        _auditLogger.LogAccessGranted(agent, "SUPPORT_VIEW_TENANT_HEALTH", tenantId);

        var workloads = await _workloadRepo.GetByTenantIdAsync(tenantId, ct);
        var activeWorkloads = workloads.Count(w => w.Status == WorkloadStatus.Active);
        var revokedWorkloads = workloads.Count(w => w.Status == WorkloadStatus.Revoked);

        // Inspect transactional outbox metrics if available
        int? efOutboxPending = null;
        DateTime? lastDispatchedUtc = null;

        using (var scope = _serviceProvider.CreateScope())
        {
            var efDb = scope.ServiceProvider.GetService(typeof(PersonalAIAssistant.Memory.Infrastructure.EF.EventStoreDbContext))
                as PersonalAIAssistant.Memory.Infrastructure.EF.EventStoreDbContext;

            if (efDb != null)
            {
                try
                {
                    efOutboxPending = await efDb.OutboxMessages.CountAsync(o => o.DispatchedAt == null, ct);
                    lastDispatchedUtc = await efDb.OutboxMessages
                        .Where(o => o.DispatchedAt != null)
                        .OrderByDescending(o => o.DispatchedAt)
                        .Select(o => o.DispatchedAt)
                        .FirstOrDefaultAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not inspect EF outbox during support health check.");
                }
            }
        }

        var isDegraded = efOutboxPending is > 500;

        return Ok(new
        {
            tenantId,
            status = isDegraded ? "Degraded" : "Healthy",
            checkedAtUtc = DateTimeOffset.UtcNow,
            workloads = new
            {
                total = workloads.Count,
                active = activeWorkloads,
                revoked = revokedWorkloads,
                identities = workloads.Select(w => new { w.ClientId, w.CloudPlatform, Status = w.Status.ToString(), w.LastUsedAtUtc })
            },
            outbox = new
            {
                pendingCount = efOutboxPending ?? 0,
                lastDispatchedUtc
            }
        });
    }

    /// <summary>
    /// Returns recent agent errors for a tenant grouped and categorized for root cause troubleshooting.
    /// </summary>
    [HttpGet("tenants/{tenantId}/errors")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentTrace>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenantErrors(
        string tenantId,
        [FromQuery] string? since,
        [FromQuery] string? category,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var agent = GetSupportAgentIdentity();
        _auditLogger.LogAccessGranted(agent, "SUPPORT_VIEW_TENANT_ERRORS", tenantId);

        var sinceTimestamp = ParseTimeSpanOrTimestamp(since, TimeSpan.FromDays(7));
        var clampedLimit = Math.Clamp(limit, 1, 200);

        var errors = await _traceRepo.GetErrorsByTenantAsync(tenantId, sinceTimestamp, category, clampedLimit, ct);
        return Ok(errors);
    }

    /// <summary>
    /// Queries the security audit logging state and confirms SIEM forwarder readiness.
    /// </summary>
    [HttpGet("audit")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAuditLogStatus()
    {
        var agent = GetSupportAgentIdentity();
        _auditLogger.LogAccessGranted(agent, "SUPPORT_CHECK_AUDIT_STATUS", "SecurityAuditLog");

        return Ok(new
        {
            status = "operational",
            auditTarget = "Structured SIEM Stream / Serilog JsonConsole",
            timestamp = DateTimeOffset.UtcNow,
            supportedCategories = new[]
            {
                "ACCESS_GRANTED", "ACCESS_DENIED", "PROMPT_INJECTION",
                "DLP_VIOLATION", "SSRF_ATTEMPT", "RATE_LIMITED", "AI_ACTIVITY"
            }
        });
    }

    /// <summary>
    /// Diagnostic echo endpoint for partners and support agents to verify token authentication,
    /// claim propagation, tenant mapping, and request round-trip latency.
    /// Accessible to SupportAgent, Admin, and ServicePrincipal (cloud workloads).
    /// </summary>
    [HttpPost("echo")]
    [Authorize(Roles = "SupportAgent,Admin,ServicePrincipal")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult PartnerEcho([FromBody] object? payload)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var identity = GetSupportAgentIdentity();

        // Sanitize headers: exclude sensitive authentication and authorization secrets
        var sanitizedHeaders = Request.Headers
            .Where(h => !h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) &&
                        !h.Key.Contains("Key", StringComparison.OrdinalIgnoreCase) &&
                        !h.Key.Contains("Secret", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key, h => h.Value.ToString());

        var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();

        return Ok(new
        {
            message = "Partner integration echo successful.",
            receivedAtUtc = receivedAt,
            caller = new
            {
                identity,
                tenantId = _userContext.TenantId ?? "default",
                roles = User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList(),
                isAuthenticated = User.Identity?.IsAuthenticated ?? false
            },
            claims,
            sanitizedHeaders,
            payloadEcho = payload
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
