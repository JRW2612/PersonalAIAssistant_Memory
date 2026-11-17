using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.AI
{
    public class AiMetricsLogger : IAiMetricsLogger
    {
        private readonly ILogger<AiMetricsLogger> _logger;
        private readonly ConcurrentBag<AiCallMetrics> _inMemoryStore = new();

        public AiMetricsLogger(ILogger<AiMetricsLogger> logger)
        {
            _logger = logger;
        }

        public void Record(AiCallMetrics metrics)
        {
            _inMemoryStore.Add(metrics);

            _logger.LogInformation(
                "AI Call [{Operation}] | Provider: {Provider} | Model: {Model} | " +
                "Tokens: {PromptTokens}P/{CompletionTokens}C ({TotalTokens}T) | " +
                "Cost: ${EstimatedCostUsd:F5} | Latency: {LatencyMs}ms | User: {UserId}",
                metrics.Operation,
                metrics.Provider,
                metrics.Model,
                metrics.PromptTokens,
                metrics.CompletionTokens,
                metrics.TotalTokens,
                metrics.EstimatedCostUsd,
                metrics.Latency.TotalMilliseconds,
                metrics.UserId ?? "system");
        }

        public Task<AiUsageSummary> GetSummaryAsync(string userId, DateTimeOffset since, CancellationToken ct)
        {
            return GetBatchSummaryAsync(new[] { userId }, since, ct);
        }

        public Task<AiUsageSummary> GetBatchSummaryAsync(IEnumerable<string> userOrWorkloadIds, DateTimeOffset since, CancellationToken ct)
        {
            var idSet = userOrWorkloadIds is HashSet<string> set
                ? set
                : new HashSet<string>(userOrWorkloadIds, StringComparer.OrdinalIgnoreCase);

            var matchedMetrics = _inMemoryStore
                .Where(m => m.UserId != null && idSet.Contains(m.UserId) && m.Timestamp >= since)
                .ToList();

            var summary = new AiUsageSummary(
                TotalPromptTokens: matchedMetrics.Sum(m => m.PromptTokens),
                TotalCompletionTokens: matchedMetrics.Sum(m => m.CompletionTokens),
                TotalEstimatedCostUsd: matchedMetrics.Sum(m => m.EstimatedCostUsd),
                CallCount: matchedMetrics.Count
            );

            return Task.FromResult(summary);
        }
    }
}
