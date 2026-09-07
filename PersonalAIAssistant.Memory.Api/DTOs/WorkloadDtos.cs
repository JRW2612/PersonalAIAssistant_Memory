namespace PersonalAIAssistant.Memory.Api.DTOs
{
    public sealed record RegisterWorkloadRequestDto(
        string ClientId,
        string Name,
        string CloudPlatform, // "GitHub", "AWS", "Azure", "GCP", "GenericM2M"
        string AuthType,      // "OidcWorkloadIdentity", "ClientCredentials", "HmacWebhook"
        string? TenantId,
        string? AllowedScopes,
        string? ClientSecret,
        string? OidcIssuer,
        string? OidcAudience,
        string? OidcSubjectFilter,
        string? WebhookSecret
    );

    public sealed record WorkloadIdentityResponseDto(
        Guid Id,
        string ClientId,
        string Name,
        string CloudPlatform,
        string AuthType,
        string TenantId,
        string AllowedScopes,
        string Status,
        string? OidcIssuer,
        string? OidcAudience,
        string? OidcSubjectFilter,
        DateTime CreatedAtUtc,
        DateTime? LastUsedAtUtc
    );

    public sealed record M2MTokenRequestDto(
        string? GrantType, // "client_credentials" or "oidc"
        string? ClientId,
        string? ClientSecret,
        string? OidcToken,
        string? Scope
    );

    public sealed record M2MTokenResponseDto(
        string AccessToken,
        string TokenType,
        int ExpiresIn,
        string Scope,
        string ClientId,
        string TenantId
    );

    public sealed record RevokeWorkloadRequestDto(
        string? Reason
    );
}
