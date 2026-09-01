using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Core.Exceptions
{
    /// <summary>Thrown when memory content violates corporate DLP policy.</summary>
    public sealed class DlpViolationException : Exception
    {
        public IReadOnlyList<DlpViolation> Violations { get; }

        public DlpViolationException(IReadOnlyList<DlpViolation> violations)
            : base(BuildMessage(violations))
        {
            Violations = violations;
        }

        private static string BuildMessage(IReadOnlyList<DlpViolation> violations)
        {
            var categories = string.Join(", ", violations.Select(v => v.Category.ToString()));
            return $"DLP policy violation: memory content contains prohibited data categories: {categories}. Ingestion blocked per corporate acceptable use policy.";
        }
    }
}
