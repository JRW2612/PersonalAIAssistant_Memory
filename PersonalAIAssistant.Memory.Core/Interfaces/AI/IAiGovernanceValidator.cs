namespace PersonalAIAssistant.Memory.Core.Interfaces.AI
{
    /// <summary>
    /// ISP: Focused solely on validating AI provider calls against corporate governance policy.
    /// </summary>
    public interface IAiGovernanceValidator
    {
        /// <summary>Validates that the provider is allowed under corporate AI policy.</summary>
        /// <exception cref="InvalidOperationException">When provider violates governance policy.</exception>
        void ValidateProvider(string providerName);
        
        /// <summary>Returns HTTP headers to inject for zero-data-retention compliance.</summary>
        IReadOnlyDictionary<string, string> GetComplianceHeaders();
    }
}
