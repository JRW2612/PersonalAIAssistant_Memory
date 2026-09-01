using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.AI
{
    /// <summary>
    /// Validates AI provider usage against corporate governance policy and
    /// provides zero-data-retention compliance headers.
    /// SRP: only handles governance policy enforcement for AI providers.
    /// </summary>
    public sealed class CorporateAiGovernanceValidator : IAiGovernanceValidator
    {
        private readonly AiGovernanceOptions _opts;
        private readonly ILogger<CorporateAiGovernanceValidator> _logger;

        public CorporateAiGovernanceValidator(
            IOptions<AiGovernanceOptions> opts,
            ILogger<CorporateAiGovernanceValidator> logger)
        {
            _opts = opts.Value;
            _logger = logger;
        }

        public void ValidateProvider(string providerName)
        {
            if (_opts.AllowedProviders.Count > 0
                && !_opts.AllowedProviders.Contains(providerName, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[AI Governance] Provider '{Provider}' is not in the corporate allowlist: [{Allowed}]",
                    providerName,
                    string.Join(", ", _opts.AllowedProviders));
                throw new InvalidOperationException(
                    $"AI provider '{providerName}' is not permitted under corporate AI governance policy.");
            }

            _logger.LogDebug("[AI Governance] Provider '{Provider}' validated successfully.", providerName);
        }

        public IReadOnlyDictionary<string, string> GetComplianceHeaders()
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (_opts.EnforceZeroDataRetention)
            {
                // Enterprise zero-data-retention signal headers
                headers["X-Enterprise-Policy"] = "ZeroRetention";
                headers["X-Data-Training-Opt-Out"] = "true";
            }

            if (_opts.AllowedDataResidencyRegions.Count > 0)
            {
                headers["X-Data-Residency"] = string.Join(",", _opts.AllowedDataResidencyRegions);
            }

            return headers.AsReadOnly();
        }
    }
}
