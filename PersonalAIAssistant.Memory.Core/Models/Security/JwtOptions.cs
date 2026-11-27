namespace PersonalAIAssistant.Memory.Core.Models.Security
{
    /// <summary>
    /// Shared JWT configuration options used for both bearer token validation and SSO session token minting.
    /// </summary>
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string SecretKey { get; set; } = string.Empty;
        public string Issuer { get; set; } = "PersonalAIAssistant.Memory";
        public string Audience { get; set; } = "PersonalAIAssistant.Memory.Api";
    }
}
