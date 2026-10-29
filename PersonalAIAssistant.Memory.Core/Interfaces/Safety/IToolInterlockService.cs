using System.Text.Json;
using PersonalAIAssistant.Memory.Core.Models.Safety;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Safety;

/// <summary>
/// Pre-execution safety interlock evaluator that intercepts agent tool commands
/// before they reach hardware controllers, simulators, or external systems.
/// </summary>
public interface IToolInterlockService
{
    /// <summary>
    /// Evaluates proposed tool arguments against registered safety rules and active environment state.
    /// </summary>
    /// <param name="toolName">Target tool name being commanded.</param>
    /// <param name="arguments">JSON arguments proposed by the model.</param>
    /// <param name="mode">Interlocked (protective) vs Thin (unprotected baseline).</param>
    /// <param name="physicalState">Optional current physical state telemetry (e.g. coordinates, forces, sensor flags).</param>
    /// <returns>Allowed, Blocked, or Clamped evaluation result.</returns>
    InterlockEvaluationResult Evaluate(
        string toolName,
        JsonElement arguments,
        InterlockMode mode = InterlockMode.Interlocked,
        IDictionary<string, object>? physicalState = null);

    /// <summary>Registers an additional safety rule dynamically.</summary>
    void RegisterRule(ToolInterlockRule rule);

    /// <summary>Returns all currently active safety interlock rules.</summary>
    IReadOnlyList<ToolInterlockRule> GetRules();
}
