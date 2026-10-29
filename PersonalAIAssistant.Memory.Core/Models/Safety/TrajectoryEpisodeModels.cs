using System.Text.Json;

namespace PersonalAIAssistant.Memory.Core.Models.Safety;

/// <summary>
/// Definition of a fault or anomaly injected at a specific turn to evaluate off-nominal safety response.
/// </summary>
public sealed class FaultInjectionDefinition
{
    public string FaultType { get; init; } = string.Empty;
    public int InjectionTurn { get; init; }
    public string Description { get; init; } = string.Empty;
    public Dictionary<string, object> Parameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Telemetry, sensor, and environment state presented to the agent as an observation.
/// </summary>
public sealed class TurnObservation
{
    public Dictionary<string, object> SensorReadouts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ActiveAlerts { get; init; } = new();
    public string? VisualFrameUri { get; init; }
    public string? RawObservationSummary { get; init; }
}

/// <summary>
/// Internal cognitive model and state estimates self-reported by the agent during its reasoning phase.
/// </summary>
public sealed class TurnAgentBelief
{
    public bool? BelievesObjectHeld { get; set; }
    public bool BelievesAnomalyPresent { get; set; }
    public string? HypothesizedCause { get; set; }
    public double? Confidence { get; set; }
    public string? ReasoningSnippet { get; set; }
    public Dictionary<string, object> EstimatedCoordinates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Tool command or actuator action selected by the agent in the act phase.
/// </summary>
public sealed class TurnAction
{
    public string ToolName { get; init; } = string.Empty;
    public JsonElement Arguments { get; init; }
    public long LatencyMs { get; init; }
}

/// <summary>
/// Verifiable physical ground-truth state inside the physical system or high-fidelity simulator.
/// </summary>
public sealed class TurnGroundTruth
{
    public bool ActualObjectHeld { get; set; }
    public double ActualSpillVolumeMl { get; set; }
    public double PeakCollisionForceN { get; set; }
    public double ObjectDisplacementM { get; set; }
    public double ObjectTiltDeg { get; set; }
    public bool InterlockTripped { get; set; }
    public bool HasActiveFault { get; set; }
    public string? PhysicalStateSummary { get; set; }
    public Dictionary<string, object> ActualCoordinates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Explicit delta between the agent's internal belief and physical reality for a single turn.
/// Divergence is the fundamental root cause of physical AI safety failures identified in the micro1 research.
/// </summary>
public sealed class TurnBeliefDivergence
{
    public bool HasDivergence { get; set; }
    public bool ObjectHeldDivergence { get; set; }
    public bool AnomalyDetectionDivergence { get; set; }
    public bool SpillDetectionDivergence { get; set; }
    public List<string> DivergenceDetails { get; set; } = new();
    public string? Summary { get; set; }
}

/// <summary>
/// Full recording of one turn within the Observe -> Reason -> Act loop.
/// </summary>
public sealed class TrajectoryTurn
{
    public int TurnIndex { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public TurnObservation Observation { get; init; } = new();
    public TurnAgentBelief AgentBelief { get; init; } = new();
    public TurnAction Action { get; init; } = new();
    public InterlockEvaluationResult? InterlockResult { get; set; }
    public TurnGroundTruth GroundTruth { get; set; } = new();
    public TurnBeliefDivergence Divergence { get; set; } = new();
    public bool IsInjectedFaultTurn { get; set; }
}

/// <summary>
/// Agent's self-reported final outcome and justification.
/// </summary>
public sealed class EpisodeClaimedOutcome
{
    public bool ClaimedSuccess { get; init; }
    public string? ClaimedReport { get; init; }
    public int TerminatedAtTurn { get; init; }
    public bool SelfTerminated { get; init; }
    public string? SelfDiagnosis { get; init; }
}

/// <summary>
/// Objective simulator / physical ground-truth outcome.
/// </summary>
public sealed class EpisodeGroundTruthOutcome
{
    public bool GroundTruthSuccess { get; set; }
    public bool PhysicalDamageOccurred { get; set; }
    public double TotalSpillVolumeMl { get; set; }
    public double MaxForceN { get; set; }
    public int TotalInterlockTrips { get; set; }
    public string? FailureReason { get; set; }
}

public enum EpisodeStatus
{
    Active,
    Completed,
    Aborted,
    BudgetExhausted
}

/// <summary>
/// Complete episode preserving the full trajectory: what the agent observed, decided, and believed happened,
/// alongside what physically occurred.
/// </summary>
public sealed class AgentTrajectoryEpisode
{
    public Guid EpisodeId { get; init; } = Guid.NewGuid();
    public string TaskId { get; init; } = string.Empty;
    public string AgentModel { get; init; } = string.Empty;
    public string UserId { get; init; } = "anonymous";
    public string? TenantId { get; init; }
    public InterlockMode InterlockMode { get; init; } = InterlockMode.Interlocked;
    public int MaxStepBudget { get; init; } = 100;
    public EpisodeStatus Status { get; set; } = EpisodeStatus.Active;

    public FaultInjectionDefinition? InjectedFault { get; init; }
    public List<TrajectoryTurn> Turns { get; init; } = new();

    public EpisodeClaimedOutcome? ClaimedOutcome { get; set; }
    public EpisodeGroundTruthOutcome? GroundTruthOutcome { get; set; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public long DurationMs => CompletedAt.HasValue
        ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds
        : (long)(DateTimeOffset.UtcNow - StartedAt).TotalMilliseconds;
}
