namespace PersonalAIAssistant.Memory.Core.Models.Security
{
    /// <summary>
    /// Configuration options for Enterprise SSO / Identity Federation (OIDC & SAML 2.0).
    /// Supports Microsoft Entra ID (Azure AD), Ping Identity, and Okta.
    /// </summary>
    public sealed class EnterpriseSsoOptions
    {
        public const string SectionName = "EnterpriseSso";

        public bool Enabled { get; set; } = true;
        public string DefaultProtocol { get; set; } = "OIDC"; // "OIDC" or "SAML2"
        public string SpEntityId { get; set; } = "https://memory.ai-assistant.internal/sp";
        public string AssertionConsumerServiceUrl { get; set; } = "https://memory.ai-assistant.internal/api/v1/auth/enterprise/saml";
        public List<EnterpriseIdpConfig> Providers { get; set; } = new();
    }

    public sealed class EnterpriseIdpConfig
    {
        public string TenantId { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty; // e.g. "hdfcbank.com"
        public string Protocol { get; set; } = "OIDC";     // "OIDC" or "SAML2"
        public string Authority { get; set; } = string.Empty; // e.g. "https://login.microsoftonline.com/{tenant}/v2.0"
        public string ClientId { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public string? IdpMetadataUrl { get; set; }
        public string? SigningCertThumbprint { get; set; }
        public string? SigningPublicKeyXmlOrPem { get; set; }
        public bool RequireMfa { get; set; } = true;
        public Dictionary<string, string> ClaimsMapping { get; set; } = new();
    }
}
