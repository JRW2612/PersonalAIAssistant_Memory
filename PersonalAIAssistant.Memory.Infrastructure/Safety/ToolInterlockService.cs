using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.Safety;
using PersonalAIAssistant.Memory.Core.Models.Safety;

namespace PersonalAIAssistant.Memory.Infrastructure.Safety;

/// <summary>
/// Pre-execution safety interlock implementation providing hardware boundary enforcement,
/// keep-out zone protection, force/speed limits, and environment preconditions.
/// Supports both Interlocked (protective) and Thin (unprotected baseline) modes.
/// </summary>
public sealed class ToolInterlockService : IToolInterlockService
{
    private readonly ILogger<ToolInterlockService> _logger;
    private readonly List<ToolInterlockRule> _rules = new();
    private readonly object _lock = new();

    public ToolInterlockService(ILogger<ToolInterlockService> logger)
    {
        _logger = logger;
        RegisterDefaultRules();
    }

    private void RegisterDefaultRules()
    {
        // 1. Centrifuge rotor overspeed protection (Exp from micro1 paper: rotor shakes at 3.1g, slams bench)
        RegisterRule(new ToolInterlockRule
        {
            RuleId = "centrifuge-spin-overspeed",
            Name = "Centrifuge Rotor Rating Protection",
            Description = "Rejects spin requests beyond the rotor's rating (max 10,000 RPM) to prevent severe 3.1g drum shaking and collisions.",
            TargetTool = "centrifuge_spin",
            MaxNumericLimits = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "rpm", 10000.0 },
                { "speed_rpm", 10000.0 }
            },
            ActionOnViolation = InterlockAction.Block
        });

        // 2. Centrifuge lid interlock
        RegisterRule(new ToolInterlockRule
        {
            RuleId = "centrifuge-lid-closed",
            Name = "Centrifuge Lid Interlock",
            Description = "Enforces that the centrifuge safety lid is closed and latched before spin initiation.",
            TargetTool = "centrifuge_spin",
            RequiredPreconditions = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "lid_closed", true }
            },
            ActionOnViolation = InterlockAction.Block
        });

        // 3. Robotic arm contact force limiter (Exp 2 from paper: prevents 293 N / 327 N destructive collisions)
        RegisterRule(new ToolInterlockRule
        {
            RuleId = "arm-force-limit",
            Name = "Arm Contact Force Limiter",
            Description = "Clamps or blocks excessive arm contact force above 100 N to avoid crushing glassware and damaging bench equipment.",
            TargetTool = "arm_joint_move",
            MaxNumericLimits = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "force_n", 100.0 },
                { "contact_force", 100.0 }
            },
            ActionOnViolation = InterlockAction.Clamp
        });

        // 4. Live PCR reaction plate spatial keep-out zone (Exp 2 from paper: plate holds live reactions and must never be crossed)
        RegisterRule(new ToolInterlockRule
        {
            RuleId = "pcr-plate-keepout",
            Name = "Live PCR Plate Keep-Out Zone",
            Description = "Prohibits robot arm trajectory coordinates from entering the live PCR plate workspace bounding box.",
            TargetTool = "arm_joint_move",
            KeepOutZones = new List<SpatialBoundingBox>
            {
                new(MinX: 0.20, MaxX: 0.50, MinY: 0.30, MaxY: 0.60, MinZ: 0.0, MaxZ: 0.40)
            },
            ActionOnViolation = InterlockAction.Block
        });

        // 5. Reagent liquid handling froth settling interlock (Exp 1 from paper: froth settles before carry/stir)
        RegisterRule(new ToolInterlockRule
        {
            RuleId = "reagent-froth-settling",
            Name = "Reagent Froth Settling Interlock",
            Description = "Blocks reagent stirring or rapid carry when foam or froth has risen above the tube rim until settled.",
            TargetTool = "liquid_stir_reagent",
            RequiredPreconditions = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "froth_settled", true }
            },
            ActionOnViolation = InterlockAction.Block
        });
    }

    public void RegisterRule(ToolInterlockRule rule)
    {
        lock (_lock)
        {
            _rules.RemoveAll(r => r.RuleId.Equals(rule.RuleId, StringComparison.OrdinalIgnoreCase));
            _rules.Add(rule);
        }
    }

    public IReadOnlyList<ToolInterlockRule> GetRules()
    {
        lock (_lock)
        {
            return _rules.ToList();
        }
    }

    public InterlockEvaluationResult Evaluate(
        string toolName,
        JsonElement arguments,
        InterlockMode mode = InterlockMode.Interlocked,
        IDictionary<string, object>? physicalState = null)
    {
        var targetTool = toolName.Trim().ToLowerInvariant();
        List<ToolInterlockRule> matchingRules;

        lock (_lock)
        {
            matchingRules = _rules.Where(r =>
                r.TargetTool == "*" ||
                r.TargetTool.Equals(targetTool, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (matchingRules.Count == 0)
        {
            return InterlockEvaluationResult.Allow(mode);
        }

        var violations = new List<InterlockViolation>();
        bool requiresClamp = false;
        var clampingModifications = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in matchingRules)
        {
            // 1. Check Max Numeric Limits
            foreach (var (paramName, maxLimit) in rule.MaxNumericLimits)
            {
                if (TryGetNumericProperty(arguments, paramName, out var actualVal))
                {
                    if (actualVal > maxLimit)
                    {
                        var violation = new InterlockViolation(
                            RuleId: rule.RuleId,
                            RuleName: rule.Name,
                            ParameterName: paramName,
                            AttemptedValue: actualVal,
                            SafeThreshold: maxLimit,
                            Description: $"{paramName} value {actualVal} exceeds maximum safety limit of {maxLimit}.",
                            Timestamp: DateTimeOffset.UtcNow);

                        violations.Add(violation);

                        if (rule.ActionOnViolation == InterlockAction.Clamp)
                        {
                            requiresClamp = true;
                            clampingModifications[paramName] = maxLimit;
                        }
                    }
                }
            }

            // 2. Check Min Numeric Limits
            foreach (var (paramName, minLimit) in rule.MinNumericLimits)
            {
                if (TryGetNumericProperty(arguments, paramName, out var actualVal))
                {
                    if (actualVal < minLimit)
                    {
                        violations.Add(new InterlockViolation(
                            RuleId: rule.RuleId,
                            RuleName: rule.Name,
                            ParameterName: paramName,
                            AttemptedValue: actualVal,
                            SafeThreshold: minLimit,
                            Description: $"{paramName} value {actualVal} is below minimum safety threshold of {minLimit}.",
                            Timestamp: DateTimeOffset.UtcNow));
                    }
                }
            }

            // 3. Check Keep-Out Spatial Zones
            if (rule.KeepOutZones.Count > 0 &&
                TryGetNumericProperty(arguments, "x", out var x) &&
                TryGetNumericProperty(arguments, "y", out var y))
            {
                var z = TryGetNumericProperty(arguments, "z", out var zVal) ? zVal : 0.0;
                foreach (var box in rule.KeepOutZones)
                {
                    if (box.Contains(x, y, z))
                    {
                        violations.Add(new InterlockViolation(
                            RuleId: rule.RuleId,
                            RuleName: rule.Name,
                            ParameterName: "coordinates",
                            AttemptedValue: $"({x:F2}, {y:F2}, {z:F2})",
                            SafeThreshold: $"Outside keep-out zone [{box.MinX}..{box.MaxX}, {box.MinY}..{box.MaxY}, {box.MinZ}..{box.MaxZ}]",
                            Description: $"Target coordinates ({x:F2}, {y:F2}, {z:F2}) penetrate keep-out safety zone for '{rule.Name}'.",
                            Timestamp: DateTimeOffset.UtcNow));
                    }
                }
            }

            // 4. Check Environment Preconditions
            if (rule.RequiredPreconditions.Count > 0 && physicalState != null)
            {
                foreach (var (precondKey, expectedVal) in rule.RequiredPreconditions)
                {
                    if (physicalState.TryGetValue(precondKey, out var actualPrecondVal))
                    {
                        bool satisfied = false;
                        if (expectedVal is bool expectedBool && actualPrecondVal is bool actualBool)
                        {
                            satisfied = (expectedBool == actualBool);
                        }
                        else
                        {
                            satisfied = Equals(expectedVal?.ToString(), actualPrecondVal?.ToString());
                        }

                        if (!satisfied)
                        {
                            violations.Add(new InterlockViolation(
                                RuleId: rule.RuleId,
                                RuleName: rule.Name,
                                ParameterName: precondKey,
                                AttemptedValue: actualPrecondVal,
                                SafeThreshold: expectedVal,
                                Description: $"Precondition '{precondKey}' failed. Expected '{expectedVal}' but was '{actualPrecondVal}'.",
                                Timestamp: DateTimeOffset.UtcNow));
                        }
                    }
                }
            }
        }

        // Evaluate according to Mode
        if (violations.Count == 0)
        {
            return InterlockEvaluationResult.Allow(mode);
        }

        if (mode == InterlockMode.Thin)
        {
            // Thin mode passes commands through, but records the violations for safety auditing / comparative bench
            _logger.LogWarning("[THIN INTERLOCK BASELINE] Command '{ToolName}' tripped {Count} safety rules but permitted in Thin mode.",
                toolName, violations.Count);

            return new InterlockEvaluationResult
            {
                Allowed = true,
                Mode = InterlockMode.Thin,
                Decision = InterlockAction.Allow,
                Violations = violations,
                RejectionReason = "Violations detected but permitted in Thin mode."
            };
        }

        // Interlocked mode: Block or Clamp
        var hasBlockingViolations = violations.Any(v =>
            matchingRules.FirstOrDefault(r => r.RuleId == v.RuleId)?.ActionOnViolation != InterlockAction.Clamp);

        if (hasBlockingViolations || !requiresClamp)
        {
            var summaryReason = string.Join("; ", violations.Select(v => v.Description));
            _logger.LogWarning("[SAFETY INTERLOCK BLOCKED] Command '{ToolName}' blocked: {Reason}", toolName, summaryReason);

            return InterlockEvaluationResult.Reject(mode, violations, summaryReason);
        }

        // Clamping modifications applied
        var clonedJson = ClampArguments(arguments, clampingModifications);
        var clampReason = string.Join("; ", violations.Select(v => $"Clamped {v.ParameterName} to safe limit {v.SafeThreshold}"));
        _logger.LogInformation("[SAFETY INTERLOCK CLAMPED] Command '{ToolName}' clamped: {Reason}", toolName, clampReason);

        return InterlockEvaluationResult.Clamped(mode, violations, clonedJson, clampReason);
    }

    private static bool TryGetNumericProperty(JsonElement element, string propertyName, out double value)
    {
        value = 0.0;
        if (element.ValueKind != JsonValueKind.Object) return false;

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var num))
                {
                    value = num;
                    return true;
                }
            }
        }
        return false;
    }

    private static JsonElement ClampArguments(JsonElement original, Dictionary<string, double> clamps)
    {
        try
        {
            var node = JsonNode.Parse(original.GetRawText())?.AsObject();
            if (node == null) return original;

            foreach (var (prop, safeVal) in clamps)
            {
                foreach (var kvp in node.ToList())
                {
                    if (kvp.Key.Equals(prop, StringComparison.OrdinalIgnoreCase))
                    {
                        node[kvp.Key] = safeVal;
                    }
                }
            }

            var serialized = node.ToJsonString();
            using var doc = JsonDocument.Parse(serialized);
            return doc.RootElement.Clone();
        }
        catch
        {
            return original;
        }
    }
}
