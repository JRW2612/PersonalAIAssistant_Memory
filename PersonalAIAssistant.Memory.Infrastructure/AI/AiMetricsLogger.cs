using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;
using System.Collections.Concurrent;

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
            var userMetrics = _inMemoryStore
                .Where(m => m.UserId == userId && m.Timestamp >= since)
                .ToList();

            var summary = new AiUsageSummary(
                TotalPromptTokens: userMetrics.Sum(m => m.PromptTokens),
                TotalCompletionTokens: userMetrics.Sum(m => m.CompletionTokens),
                TotalEstimatedCostUsd: userMetrics.Sum(m => m.EstimatedCostUsd),
                CallCount: userMetrics.Count
            );

            return Task.FromResult(summary);
        }
    }
}
