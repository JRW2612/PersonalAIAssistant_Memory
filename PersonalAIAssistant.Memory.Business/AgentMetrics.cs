using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PersonalAIAssistant.Memory.Business;

/// <summary>
/// Central OpenTelemetry instrumentation singleton for all AI and agent activity.
/// Registered as a singleton in DI. Metrics are emitted to the
/// <c>PersonalAIAssistant.Memory.AI</c> meter, which is wired into the OTEL
/// <see cref="Microsoft.Extensions.DependencyInjection.MeterProviderBuilderExtensions"/>.
/// </summary>
public sealed class AgentMetrics : IDisposable
{
    public const string MeterName = "PersonalAIAssistant.Memory.AI";

    private readonly Meter _meter;

    // ── AI call counters ────────────────────────────────────────────────────
    /// <summary>Total AI provider calls. Tags: provider, model, operation, success.</summary>
    public readonly Counter<long> AiCallsTotal;

    /// <summary>Total prompt tokens consumed across all AI calls.</summary>
    public readonly Counter<long> AiPromptTokensTotal;

    /// <summary>Total completion tokens produced across all AI calls.</summary>
    public readonly Counter<long> AiCompletionTokensTotal;

    /// <summary>AI call latency histogram in milliseconds. Tags: provider, model, operation.</summary>
    public readonly Histogram<double> AiLatencyMs;

    /// <summary>Estimated USD cost per AI call. Tags: provider, model.</summary>
    public readonly Histogram<double> AiCostUsd;

    // ── Agent trace counters ────────────────────────────────────────────────
    /// <summary>Total agent traces captured. Tags: request_name, success.</summary>
    public readonly Counter<long> AgentTracesTotal;

    /// <summary>Total agent tool execution errors. Tags: error_category, request_name.</summary>
    public readonly Counter<long> AgentToolErrorsTotal;

    /// <summary>Agent request pipeline duration histogram in milliseconds. Tags: request_name.</summary>
    public readonly Histogram<double> AgentRequestDurationMs;

    public AgentMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        AiCallsTotal = _meter.CreateCounter<long>(
            "ai.calls.total",
            unit: "{calls}",
            description: "Total AI provider calls.");

        AiPromptTokensTotal = _meter.CreateCounter<long>(
            "ai.tokens.prompt",
            unit: "{tokens}",
            description: "Total prompt tokens consumed across all AI calls.");

        AiCompletionTokensTotal = _meter.CreateCounter<long>(
            "ai.tokens.completion",
            unit: "{tokens}",
            description: "Total completion tokens produced across all AI calls.");

        AiLatencyMs = _meter.CreateHistogram<double>(
            "ai.latency.ms",
            unit: "ms",
            description: "AI provider call latency in milliseconds.");

        AiCostUsd = _meter.CreateHistogram<double>(
            "ai.cost.usd",
            unit: "$",
            description: "Estimated USD cost per AI provider call.");

        AgentTracesTotal = _meter.CreateCounter<long>(
            "agent.traces.total",
            unit: "{traces}",
            description: "Total agent execution traces captured.");

        AgentToolErrorsTotal = _meter.CreateCounter<long>(
            "agent.tool.errors.total",
            unit: "{errors}",
            description: "Total agent tool execution failures.");

        AgentRequestDurationMs = _meter.CreateHistogram<double>(
            "agent.request.duration.ms",
            unit: "ms",
            description: "End-to-end agent pipeline request duration in milliseconds.");
    }

    /// <summary>
    /// Convenience method to record a completed AI call from any infrastructure provider.
    /// </summary>
    public void RecordAiCall(
        string provider, string model, string operation,
        bool success, long latencyMs, int promptTokens, int completionTokens, double costUsd)
    {
        var tags = new TagList
        {
            { "provider", provider },
            { "model", model },
            { "operation", operation },
            { "success", success.ToString().ToLowerInvariant() }
        };

        AiCallsTotal.Add(1, tags);
        AiPromptTokensTotal.Add(promptTokens, new TagList { { "provider", provider }, { "model", model } });
        AiCompletionTokensTotal.Add(completionTokens, new TagList { { "provider", provider }, { "model", model } });
        AiLatencyMs.Record(latencyMs, new TagList { { "provider", provider }, { "model", model }, { "operation", operation } });
        AiCostUsd.Record(costUsd, new TagList { { "provider", provider }, { "model", model } });
    }

    /// <summary>
    /// Convenience method to record a completed agent trace.
    /// </summary>
    public void RecordTrace(string requestName, bool success, long durationMs, string? errorCategory = null)
    {
        AgentTracesTotal.Add(1, new TagList
        {
            { "request_name", requestName },
            { "success", success.ToString().ToLowerInvariant() }
        });

        AgentRequestDurationMs.Record(durationMs, new TagList { { "request_name", requestName } });

        if (!success && !string.IsNullOrEmpty(errorCategory))
        {
            AgentToolErrorsTotal.Add(1, new TagList
            {
                { "error_category", errorCategory },
                { "request_name", requestName }
            });
        }
    }

    public void Dispose() => _meter.Dispose();
}