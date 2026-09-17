namespace PersonalAIAssistant.Memory.Core.Models
{
    /// <summary>
    /// Categorises the root cause of a failed agent tool invocation for
    /// structured error analysis and dashboarding.
    /// </summary>
    public enum AgentErrorCategory
    {
        None,
        ValidationFailure,
        PromptInjection,
        DlpViolation,
        SsrfViolation,
        InputSanitization,
        AiProviderError,
        Timeout,
        Concurrency,
        NotFound,
        Unauthorized,
        Unknown
    }

    /// <summary>
    /// Timing record for a single MediatR pipeline behavior step within an agent request.
    /// </summary>
    public sealed record BehaviorStep(
        string BehaviorName,
        long ElapsedMs,
        bool Passed,
        string? Detail = null);

    /// <summary>
    /// Full execution trace for one agent tool invocation captured by <c>AgentTracingBehavior</c>.
    /// </summary>
    public sealed class AgentTrace
    {
        public Guid TraceId { get; init; } = Guid.NewGuid();
        public string UserId { get; init; } = string.Empty;
        public string? TenantId { get; init; }
        public string? CorrelationId { get; init; }

        /// <summary>MediatR request type name (e.g. AddMemoryCommand, RetrieveMemoriesQuery).</summary>
        public string RequestName { get; init; } = string.Empty;

        /// <summary>Agent tool name when the trace originates from AgentToolService.ExecuteToolAsync.</summary>
        public string? ToolName { get; init; }

        /// <summary>JSON-serialized request payload (PII redacted by the behavior).</summary>
        public string? InputJson { get; init; }

        /// <summary>JSON-serialized response payload (truncated to 4 KB).</summary>
        public string? OutputJson { get; set; }

        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; set; }
        public long DurationMs => CompletedAt.HasValue
            ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds
            : 0;

        public bool Success { get; set; }
        public AgentErrorCategory ErrorCategory { get; set; } = AgentErrorCategory.None;
        public string? ErrorDetail { get; set; }

        /// <summary>Ordered timing records for each pipeline behavior that ran.</summary>
        public List<BehaviorStep> BehaviorSteps { get; init; } = new();

        /// <summary>OpenTelemetry trace ID of the ambient Activity when the request was processed.</summary>
        public string? OtelTraceId { get; init; }
    }

    /// <summary>
    /// Configuration bound from <c>appsettings.json</c> section <c>"AgentTracing"</c>.
    /// </summary>
    public sealed class AgentTracingOptions
    {
        public const string SectionName = "AgentTracing";

        /// <summary>Master switch — set false to disable all trace capture.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Maximum traces held in the in-memory ring buffer before FIFO eviction begins.</summary>
        public int MaxTraces { get; set; } = 10_000;

        /// <summary>
        /// How long to retain traces. Default 168 h (7 days).
        /// Recommended: 24 h for dev, 720 h (30 days) for regulated environments.
        /// </summary>
        public int RetentionHours { get; set; } = 168;
    }
}