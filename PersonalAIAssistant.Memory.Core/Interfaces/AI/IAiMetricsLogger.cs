using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Core.Interfaces.AI
{
    public interface IAiMetricsLogger
    {
        /// <summary>Records a single AI provider call with token usage and latency.</summary>
        void Record(AiCallMetrics metrics);

        /// <summary>Returns an aggregated usage summary for the given user since the specified timestamp.</summary>
        Task<AiUsageSummary> GetSummaryAsync(string userId, DateTimeOffset since, CancellationToken ct);
    }
}
