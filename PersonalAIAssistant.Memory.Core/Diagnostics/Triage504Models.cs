using System.Text.Json.Serialization;

namespace PersonalAIAssistant.Memory.Core.Diagnostics;

/// <summary>
/// The network/execution hop where the timeout or bottleneck originated.
/// Enables deterministic binary isolation between the client, gateway proxy, and database tiers.
/// </summary>
public enum TimeoutHop
{
    ClientToGateway,
    GatewayToService,
    ServiceToDatabase
}

/// <summary>
/// Fine-grained root-cause classification for 504 Gateway Timeout incidents.
/// </summary>
public enum TimeoutReason
{
    // Client-to-Gateway root causes
    TlsHandshakeTimeout,
    SlowClientUpload,
    ClientNetworkAbort,
    ClientPayloadTimeout,

    // Gateway-to-Service root causes
    ProxyTimeout,
    ThreadPoolStarvation,
    ServiceQueueBacklog,
    GatewayConnectionPoolExhaustion,
    ServiceUnresponsive,

    // Service-to-Database root causes
    DbQueryTimeout,
    DbDeadlock,
    DbLockWait,
    DbConnectionPoolExhaustion,
    UnindexedScanSuspected,

    // Fallback
    Unknown
}

/// <summary>
/// Granular duration measurements across each boundary hop in milliseconds.
/// </summary>
public record HopTimingMetrics(
    [property: JsonPropertyName("clientToGatewayMs")] double? ClientToGatewayMs,
    [property: JsonPropertyName("gatewayToServiceMs")] double? GatewayToServiceMs,
    [property: JsonPropertyName("serviceToDatabaseMs")] double? ServiceToDatabaseMs,
    [property: JsonPropertyName("totalElapsedMs")] double TotalElapsedMs
)
{
    /// <summary>
    /// Generates the standard W3C Server-Timing header value.
    /// Example: "c2g;dur=15.2, g2s;dur=120.4, s2db;dur=5002.8"
    /// </summary>
    public string ToServerTimingHeader()
    {
        var parts = new List<string>(3);
        if (ClientToGatewayMs.HasValue)
            parts.Add($"c2g;dur={ClientToGatewayMs.Value:F1}");
        if (GatewayToServiceMs.HasValue)
            parts.Add($"g2s;dur={GatewayToServiceMs.Value:F1}");
        if (ServiceToDatabaseMs.HasValue)
            parts.Add($"s2db;dur={ServiceToDatabaseMs.Value:F1}");
        return string.Join(", ", parts);
    }
}

/// <summary>
/// Diagnostic details included in the RFC 7807 504 response.
/// </summary>
public record TriageDiagnosticsInfo(
    [property: JsonPropertyName("dbQueryTag")] string? DbQueryTag,
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("remediation")] string? Remediation,
    [property: JsonPropertyName("offendingCommand")] string? OffendingCommand
);

/// <summary>
/// Output of the binary isolation triage classifier.
/// </summary>
public record TimeoutTriageResult(
    TimeoutHop Hop,
    TimeoutReason Reason,
    string Detail,
    HopTimingMetrics Metrics,
    string? DbQueryTag = null,
    string? Remediation = null,
    string? ErrorCode = null,
    string? OffendingCommand = null
);

/// <summary>
/// RFC 7807 compliant ProblemDetails response for HTTP 504 Gateway Timeout errors.
/// </summary>
public class Triage504ProblemDetails
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "https://httpstatuses.com/504";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Gateway Timeout";

    [JsonPropertyName("status")]
    public int Status { get; set; } = 504;

    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;

    [JsonPropertyName("traceparent")]
    public string Traceparent { get; set; } = string.Empty;

    [JsonPropertyName("timeoutPhase")]
    public string TimeoutPhase { get; set; } = string.Empty;

    [JsonPropertyName("timeoutReason")]
    public string TimeoutReason { get; set; } = string.Empty;

    [JsonPropertyName("hopMetrics")]
    public HopTimingMetrics HopMetrics { get; set; } = new(null, null, null, 0);

    [JsonPropertyName("diagnostics")]
    public TriageDiagnosticsInfo Diagnostics { get; set; } = new(null, null, null, null);
}

/// <summary>
/// Scoped request context tracking execution timings and database telemetry.
/// </summary>
public interface IRequestTimingContext
{
    double? ClientToGatewayMs { get; set; }
    double? GatewayToServiceMs { get; set; }
    double ServiceToDatabaseMs { get; }
    string? LastExecutedQueryTag { get; set; }
    string? LastExecutedCommandText { get; set; }
    Exception? LastDatabaseException { get; set; }
    int DatabaseQueryCount { get; }

    void RecordDatabaseExecution(TimeSpan duration, string? queryTag, string? commandText);
    void RecordDatabaseFailure(Exception ex, string? queryTag, string? commandText);
    HopTimingMetrics GetMetrics();
}
