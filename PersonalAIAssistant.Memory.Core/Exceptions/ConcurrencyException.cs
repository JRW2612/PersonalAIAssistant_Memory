namespace PersonalAIAssistant.Memory.Core.Exceptions
{
    /// <summary>
    /// Raised when optimistic concurrency check fails while appending events.
    /// </summary>
    public class ConcurrencyException : Exception
    {
        public ConcurrencyException() { }
        public ConcurrencyException(string message) : base(message) { }
        public ConcurrencyException(string message, Exception inner) : base(message, inner) { }

    }
}
