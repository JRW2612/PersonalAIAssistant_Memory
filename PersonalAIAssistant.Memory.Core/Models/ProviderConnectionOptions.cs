namespace PersonalAIAssistant.Memory.Core.Models;

public sealed class ProviderConnectionOptions
{
    public const string SectionName = "ProviderConnections";
    public bool EnableOAuthSimulation { get; set; }
    public int OAuthStateLifetimeMinutes { get; set; } = 10;
    public GoogleOAuthOptions Google { get; set; } = new();
    public MicrosoftOAuthOptions Microsoft { get; set; } = new();
    public ZoomOAuthOptions Zoom { get; set; } = new();
}

public sealed class GoogleOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string[] OAuthScopes { get; set; } = ["openid", "email", "profile"];
}

public sealed class MicrosoftOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Tenant { get; set; } = "common";
    public string[] TranscriptScopes { get; set; } = ["OnlineMeetingTranscript.Read.All", "offline_access"];
}

public sealed class ZoomOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string[] TranscriptScopes { get; set; } = ["cloud_recording:read:meeting_transcript"];
}
