namespace PersonalAIAssistant.Memory.Core.Models
{
    // ─────────────────────────────────────────────
    // Top-level AI router options
    // ─────────────────────────────────────────────

    /// <summary>
    /// Root AI configuration. Bound from the "AI" section of appsettings.
    /// </summary>
    public sealed class AiProviderOptions
    {
        public const string SectionName = "AI";

        /// <summary>
        /// The provider name to use when no per-request override is supplied.
        /// Valid values: "openai", "gemini".
        /// </summary>
        public string Default { get; set; } = "openai";

        /// <summary>Whether AI calls are enabled at all (kill-switch).</summary>
        public bool Enabled { get; set; } = true;

        public OpenAiOptions OpenAi { get; set; } = new();
        public GeminiOptions Gemini { get; set; } = new();
        public ChunkingOptions Chunking { get; set; } = new();
    }

    // ─────────────────────────────────────────────
    // Provider-specific options
    // ─────────────────────────────────────────────

    /// <summary>
    /// OpenAI provider settings. Bind API key from user-secrets / env var OPENAI__APIKEY.
    /// </summary>
    public sealed class OpenAiOptions
    {
        public const string SectionName = "AI:OpenAi";

        /// <summary>Your OpenAI API key (sk-...).</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Model used by ConsolidateMemoriesCommandHandler (high-quality, larger model).
        /// Default: gpt-4o-mini.
        /// </summary>
        public string ConsolidationModel { get; set; } = "gpt-4o-mini";

        /// <summary>
        /// Model used by CompressMemoryCommandHandler (faster, cheaper model).
        /// Default: gpt-3.5-turbo.
        /// </summary>
        public string CompressionModel { get; set; } = "gpt-3.5-turbo";

        /// <summary>Maximum tokens to request in the completion.</summary>
        public int MaxTokens { get; set; } = 1024;

        /// <summary>Sampling temperature (0 = deterministic, 1 = creative).</summary>
        public double Temperature { get; set; } = 0.3;
    }

    /// <summary>
    /// Gemini (Google Generative Language API) provider settings.
    /// Bind API key from user-secrets / env var GEMINI__APIKEY.
    /// </summary>
    public sealed class GeminiOptions
    {
        public const string SectionName = "AI:Gemini";

        /// <summary>Your Gemini API key (AIza...).</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Model for consolidation tasks.</summary>
        public string ConsolidationModel { get; set; } = "gemini-1.5-pro";

        /// <summary>Model for compression tasks (cheaper tier).</summary>
        public string CompressionModel { get; set; } = "gemini-1.5-flash";

        /// <summary>Maximum output tokens.</summary>
        public int MaxOutputTokens { get; set; } = 1024;

        /// <summary>Sampling temperature.</summary>
        public double Temperature { get; set; } = 0.3;
    }

    // ─────────────────────────────────────────────
    // Teams options
    // ─────────────────────────────────────────────

    /// <summary>
    /// Microsoft Teams Incoming Webhook settings. Bound from "Teams" section.
    /// </summary>
    public sealed class TeamsOptions
    {
        public const string SectionName = "Teams";

        /// <summary>
        /// Full webhook URL from the Teams Incoming Webhook connector.
        /// Example: https://outlook.office.com/webhook/...
        /// </summary>
        public string WebhookUrl { get; set; } = string.Empty;

        /// <summary>Whether Teams notifications are enabled.</summary>
        public bool Enabled { get; set; } = true;
    }

    public sealed class ChunkingOptions
    {
        public bool Enabled { get; set; } = true;
        public int MaxTokens { get; set; } = 512;
        public int OverlapTokens { get; set; } = 64;
    }
}
