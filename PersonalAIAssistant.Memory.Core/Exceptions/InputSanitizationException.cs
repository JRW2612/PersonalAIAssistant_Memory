namespace PersonalAIAssistant.Memory.Core.Exceptions
{
    /// <summary>
    /// Thrown when memory payload violates input sanitization rules (e.g. exceeds length, contains null bytes or malicious control sequences).
    /// </summary>
    public sealed class InputSanitizationException : Exception
    {
        public string ViolationType { get; }

        public InputSanitizationException(string violationType, string message)
            : base(message)
        {
            ViolationType = violationType;
        }
    }
}
