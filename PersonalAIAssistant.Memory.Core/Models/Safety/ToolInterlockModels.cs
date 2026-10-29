using System.Text.Json;

namespace PersonalAIAssistant.Memory.Core.Models.Safety;

/// <summary>
/// Operational mode for the tool interlock layer.
/// </summary>
public enum InterlockMode
{
    /// <summary>
    /// Protective production mode: interlocks enforce safety envelopes, blocking or clamping unsafe commands.
    /// </summary>
    Interlocked,

    /// <summary>
    /// Unprotected baseline mode: commands pass directly through to hardware or simulator without safeguards,
    /// enabling comparative hazard benchmarking (as demonstrated in the micro1 research).
    /// </summary>
    Thin
}

/// <summary>
/// Resulting action decided by an interlock rule.
/// </summary>
public enum InterlockAction
{
    Allow,
    Block,
    Clamp,
    RequireHumanApproval
}

/// <summary>
/// Details of an interlock rule violation.
/// </summary>
public sealed record InterlockViolation(
    string RuleId,
    string RuleName,
    string ParameterName,
    object? AttemptedValue,
    object? SafeThreshold,
    string Description,
    DateTimeOffset Timestamp);

/// <summary>
/// Spatial 3D bounding box for workspace keep-out zones.
/// </summary>
public sealed record SpatialBoundingBox(
    double MinX, double MaxX,
    double MinY, double MaxY,
    double MinZ, double MaxZ)
{
    public bool Contains(double x, double y, double z) =>
        x >= MinX && x <= MaxX &&
        y >= MinY && y <= MaxY &&
        z >= MinZ && z <= MaxZ;
}

/// <summary>
/// Safety rule specification governing a software-addressable tool or physical actuator.
/// </summary>
public sealed class ToolInterlockRule
{
    public string RuleId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Tool name to which this rule applies (e.g. "centrifuge_spin", "arm_joint_move", "liquid_stir_reagent").
    /// Wildcard "*" applies to all tools.
    /// </summary>
    public string TargetTool { get; init; } = "*";

    /// <summary>Maximum allowed numeric parameter thresholds (e.g. "rpm" => 10000.0, "force_n" => 100.0).</summary>
    public Dictionary<string, double> MaxNumericLimits { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Minimum allowed numeric parameter thresholds.</summary>
    public Dictionary<string, double> MinNumericLimits { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keep-out bounding boxes that must not be crossed or targeted.</summary>
    public List<SpatialBoundingBox> KeepOutZones { get; init; } = new();

    /// <summary>Required environment preconditions (e.g. "lid_closed" => true, "froth_settled" => true).</summary>
    public Dictionary<string, object> RequiredPreconditions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Action to execute when this rule is violated in Interlocked mode.</summary>
    public InterlockAction ActionOnViolation { get; init; } = InterlockAction.Block;
}

/// <summary>
/// Complete outcome of evaluating proposed tool arguments against active interlock rules.
/// </summary>
public sealed class InterlockEvaluationResult
{
    public bool Allowed { get; init; }
    public InterlockMode Mode { get; init; }
    public InterlockAction Decision { get; init; }
    public List<InterlockViolation> Violations { get; init; } = new();
    public string? RejectionReason { get; init; }
    public JsonElement? ModifiedArguments { get; init; }

    public static InterlockEvaluationResult Allow(InterlockMode mode) => new()
    {
        Allowed = true,
        Mode = mode,
        Decision = InterlockAction.Allow
    };

    public static InterlockEvaluationResult Reject(
        InterlockMode mode,
        List<InterlockViolation> violations,
        string reason) => new()
    {
        Allowed = false,
        Mode = mode,
        Decision = InterlockAction.Block,
        Violations = violations,
        RejectionReason = reason
    };

    public static InterlockEvaluationResult Clamped(
        InterlockMode mode,
        List<InterlockViolation> violations,
        JsonElement modifiedArgs,
        string reason) => new()
    {
        Allowed = true,
        Mode = mode,
        Decision = InterlockAction.Clamp,
        Violations = violations,
        ModifiedArguments = modifiedArgs,
        RejectionReason = reason
    };
}

/// <summary>
/// Exception thrown when a hardware or physical tool command trips an interlock in Interlocked mode.
/// </summary>
public class InterlockViolationException : Exception
{
    public string ToolName { get; }
    public IReadOnlyList<InterlockViolation> Violations { get; }

    public InterlockViolationException(string toolName, IReadOnlyList<InterlockViolation> violations, string message)
        : base(message)
    {
        ToolName = toolName;
        Violations = violations;
    }
}
