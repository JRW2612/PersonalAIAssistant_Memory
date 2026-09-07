namespace PersonalAIAssistant.Memory.Core.Entities;

public enum ProviderConnectionStatus
{
    Active,
    Expired,
    Revoked
}

public enum ProviderCredentialKind
{
    OAuthBearerToken,
    ApiKey
}

/// <summary>Encrypted, user-owned provider credential. No plaintext token belongs in this entity.</summary>
public sealed class ProviderConnectionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = "default";
    public string Provider { get; set; } = string.Empty;
    public string? EncryptedAccessToken { get; set; }
    public string? EncryptedRefreshToken { get; set; }
    public DateTime? TokenExpiresAtUtc { get; set; }
    public string ConsentScopes { get; set; } = string.Empty;
    public ProviderConnectionStatus Status { get; set; } = ProviderConnectionStatus.Active;
    public ProviderCredentialKind CredentialKind { get; set; } = ProviderCredentialKind.ApiKey;
    public string? AccountEmail { get; set; }
    public string? AccountName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevocationReason { get; set; }
}
