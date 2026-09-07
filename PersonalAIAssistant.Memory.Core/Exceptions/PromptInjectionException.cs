using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Core.Exceptions
{
    /// <summary>
    /// Thrown when memory text contains adversarial prompt injection, jailbreaks, or system override patterns.
    /// </summary>
    public sealed class PromptInjectionException : Exception
    {
        public InjectionCategory Category { get; }
        public string? MatchedPattern { get; }

        public PromptInjectionException(InjectionCategory category, string? matchedPattern, string message)
            : base(message)
        {
            Category = category;
            MatchedPattern = matchedPattern;
        }
    }
}
