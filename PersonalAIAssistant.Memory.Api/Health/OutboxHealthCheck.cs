using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PersonalAIAssistant.Memory.Infrastructure.Context;

namespace PersonalAIAssistant.Memory.Api.Health;

public class OutboxHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;
    private const int WarningBacklogThreshold = 100;
    private const int CriticalBacklogThreshold = 500;
    private static readonly TimeSpan MaxLagAllowed = TimeSpan.FromMinutes(3);

    public OutboxHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetService<ReadModelDbContext>();
        if (db == null)
        {
            return HealthCheckResult.Healthy("Outbox health check skipped (database context not available).");
        }

        try
        {
            var pendingStats = await db.OutboxMessages
                .Where(m => m.DispatchedAt == null)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    OldestUtc = g.Min(m => m.OccurredAt)
                })
                .FirstOrDefaultAsync(ct);

            var pendingCount = pendingStats?.Count ?? 0;
            var oldestUtc = pendingStats?.OldestUtc ?? DateTime.UtcNow;
            var currentLag = DateTime.UtcNow - oldestUtc;

            var data = new Dictionary<string, object>
            {
                { "PendingOutboxCount", pendingCount },
                { "OldestMessageAgeSeconds", currentLag.TotalSeconds }
            };

            if (pendingCount >= CriticalBacklogThreshold || currentLag > MaxLagAllowed * 2)
            {
                return HealthCheckResult.Unhealthy(
                    $"Outbox backlog CRITICAL: {pendingCount} pending messages, lag: {currentLag.TotalSeconds:F0}s",
                    data: data);
            }

            if (pendingCount >= WarningBacklogThreshold || currentLag > MaxLagAllowed)
            {
                return HealthCheckResult.Degraded(
                    $"Outbox backlog DEGRADED: {pendingCount} pending messages, lag: {currentLag.TotalSeconds:F0}s",
                    data: data);
            }

            return HealthCheckResult.Healthy("Outbox is healthy and processing within operational limits.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded($"Unable to check outbox table: {ex.Message}");
        }
    }
}
