namespace PersonalAIAssistant.Memory.Core.Interfaces.AI
{
    /// <summary>
    /// Factory for resolving an IAIProvider by name at runtime.
    /// </summary>
    public interface IAIProviderFactory
    {
        /// <summary>
        /// Returns the named provider, or the default provider if <paramref name="providerName"/> is null/empty.
        /// </summary>
        /// <param name="providerName">Optional provider name override (e.g. "gemini", "openai").</param>
        IAIProvider GetProvider(string? providerName = null);
    }
}
