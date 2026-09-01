namespace PersonalAIAssistant.Memory.Core.Models
{
    /// <summary>Corporate AI vendor governance policy configuration.</summary>
    public sealed class AiGovernanceOptions
    {
        public const string SectionName = "AiGovernance";
        /// <summary>When true, enterprise zero-data-retention headers are injected into all AI API calls.</summary>
        public bool EnforceZeroDataRetention { get; set; } = true;
        /// <summary>Allowed data residency regions (e.g. "EU", "US"). Empty = no restriction.</summary>
        public IList<string> AllowedDataResidencyRegions { get; set; } = new List<string>();
        /// <summary>Allowed AI provider names (e.g. "openai", "gemini"). Empty = all allowed.</summary>
        public IList<string> AllowedProviders { get; set; } = new List<string>();
    }
}
