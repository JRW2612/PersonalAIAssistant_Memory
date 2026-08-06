namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    /// <summary>
    /// Abstraction over any chat/completion AI provider (OpenAI, Gemini, Azure OpenAI, etc.).
    /// Handlers depend only on this interface and are unaware of which SDK or HTTP client is used.
    /// </summary>
    public interface IAIProvider
    {
        /// <summary>Unique, lowercase provider name — e.g. "openai", "gemini".</summary>
        string ProviderName { get; }

        /// <summary>
        /// Sends a prompt to the underlying model and returns the assistant reply text.
        /// </summary>
        /// <param name="prompt">Full prompt / user message to send.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<string> GetResponseAsync(string prompt, CancellationToken ct);
    }
}
