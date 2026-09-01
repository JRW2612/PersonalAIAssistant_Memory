namespace PersonalAIAssistant.Memory.Core.Models
{
    /// <summary>Customer-Managed Encryption Key (CMEK) configuration.</summary>
    public sealed class CmekOptions
    {
        public const string SectionName = "Cmek";
        public bool Enabled { get; set; } = false;
        public string? KeyVaultUri { get; set; }
        public string? KeyIdentifier { get; set; }
        public bool TenantDerivedKeys { get; set; } = true;
    }
}
