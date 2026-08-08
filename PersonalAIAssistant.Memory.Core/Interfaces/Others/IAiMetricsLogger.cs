using PersonalAIAssistant.Memory.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    public interface IAiMetricsLogger
    {
        void Record(AiCallMetrics metrics);
        Task<AiUsageSummary> GetSummaryAsync(string userId, DateTimeOffset since, CancellationToken ct);
    }
}
