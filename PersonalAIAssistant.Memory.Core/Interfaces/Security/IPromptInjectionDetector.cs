namespace PersonalAIAssistant.Memory.Core.Interfaces.Security
{
    /// <summary>
    /// Contract for scanning memory text for prompt injection, jailbreaks, and adversarial manipulation.
    /// Follows ISP: single focused concern of prompt injection detection.
    /// </summary>
    public interface IPromptInjectionDetector
    {
        PromptInjectionScanResult Scan(string text);
    }

    public sealed record PromptInjectionScanResult(
        bool IsInjection,
        string? MatchedPattern,
        InjectionCategory Category,
        string Description);

    public enum InjectionCategory
    {
        None,
        SystemPromptOverride,
        RoleConfusion,
        JailbreakAttempt,
        DataExfiltration,
        ContextManipulation
    }
}
