namespace PersonalAIAssistant.Memory.Core.Models
{
    /// <summary>
    /// Configuration options for autonomous AI exploit defense and threat mitigation.
    /// </summary>
    public sealed class AiThreatOptions
    {
        public const string SectionName = "AiThreat";

        public bool EnablePromptInjectionDetection { get; set; } = true;
        public bool BlockOnInjection { get; set; } = true;

        public bool EnableInputSanitization { get; set; } = true;
        public int MaxInputLengthChars { get; set; } = 50_000;
        public bool StripControlCharacters { get; set; } = true;
        public bool NormalizeUnicode { get; set; } = true;

        // SSRF Protection
        public bool EnableSsrfProtection { get; set; } = true;
        public bool BlockOnSsrf { get; set; } = true;
        public bool RequireHttps { get; set; } = true;
        public List<string> AllowedWebhookHosts { get; set; } = ["outlook.office.com", "outlook.office365.com"];
    }
}
