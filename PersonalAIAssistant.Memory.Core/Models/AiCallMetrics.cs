namespace PersonalAIAssistant.Memory.Core.Models
{
    public sealed record AiCallMetrics(
        string Provider,
        string Model,
        string Operation,      // "consolidation" | "compression" | "retrieval-embed"
        int PromptTokens,
        int CompletionTokens,
        int TotalTokens,
        double EstimatedCostUsd,
        TimeSpan Latency,
        bool WasCacheHit,
        bool WasFallback,
        string? UserId,
        DateTime Timestamp);

    public record AiUsageSummary(
        int TotalPromptTokens,
        int TotalCompletionTokens,
        double TotalEstimatedCostUsd,
        int CallCount);
}
