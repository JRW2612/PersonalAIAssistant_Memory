namespace PersonalAIAssistant.Memory.Core.Models;

public sealed class AiGatewayOptions
{
    public const string SectionName = "AiGateway";
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://ai-gateway:8080/v1/";
    public string InternalAuthToken { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
}
