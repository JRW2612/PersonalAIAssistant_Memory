namespace PersonalAIAssistant.Memory.Core.Entities;

/// <summary>Append-only lifecycle audit record; Details must not contain credentials or raw token material.</summary>
public sealed class ProviderAuditLogEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = "default";
    public string Provider { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string PreviousHash { get; set; } = string.Empty;
    public string EntryHash { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
