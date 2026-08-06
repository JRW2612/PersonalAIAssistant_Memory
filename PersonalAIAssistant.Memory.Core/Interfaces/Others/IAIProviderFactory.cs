namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    /// <summary>
    /// Selects the correct <see cref="IAIProvider"/> for a given request.
    /// Resolves provider by explicit name, falls back to the configured default.
    /// </summary>
    public interface IAIProviderFactory
    {
        /// <summary>
        /// Returns the provider registered under <paramref name="providerName"/>.
        /// Pass <c>null</c> to use the default provider set in <c>AiProviderOptions.Default</c>.
        /// </summary>
        IAIProvider GetProvider(string? providerName = null);
    }
}
