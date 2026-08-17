using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.AI
{
    /// <summary>
    /// Default IAIProviderFactory implementation.
    /// Resolves providers from the DI container by matching ProviderName.
    /// Supports an optional per-request name override; falls back to AiProviderOptions.Default.
    /// </summary>
    public sealed class AIProviderFactory : IAIProviderFactory
    {
        private readonly IEnumerable<IAIProvider> _providers;
        private readonly AiProviderOptions _opts;

        public AIProviderFactory(
            IEnumerable<IAIProvider> providers,
            IOptions<AiProviderOptions> opts)
        {
            _providers = providers;
            _opts      = opts.Value;
        }

        /// <inheritdoc />
        public IAIProvider GetProvider(string? providerName = null)
        {
            var name = (providerName ?? _opts.Default).Trim().ToLowerInvariant();

            var provider = _providers.FirstOrDefault(p =>
                p.ProviderName.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (provider is null)
            {
                throw new InvalidOperationException(
                    $"No IAIProvider registered with ProviderName '{name}'. " +
                    $"Available: {string.Join(", ", _providers.Select(p => p.ProviderName))}");
            }

            return provider;
        }
    }
}
