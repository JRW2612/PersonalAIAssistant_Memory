namespace PersonalAIAssistant.Memory.Api.DTOs
{
    public sealed record ProviderConnectionDto(
        string Provider,
        string Status,
        string CredentialKind,
        IReadOnlyList<string> ConsentScopes,
        DateTime? TokenExpiresAtUtc,
        string? AccountEmail,
        string? AccountName,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        bool IsActive
    );

    public sealed record DirectConnectRequestDto(
        string? ApiKey,
        string? AccessToken,
        string? RefreshToken,
        DateTime? ExpiresAtUtc,
        List<string>? Scopes,
        string? AccountEmail,
        string? AccountName
    );

    public sealed record RevokeConnectionRequestDto(
        string? Reason
    );

    public sealed record ProviderAuditLogDto(
        Guid Id,
        string Provider,
        string EventType,
        string Details,
        string? IpAddress,
        DateTime TimestampUtc
    );

    public sealed record AuthorizeResponseDto(
        string AuthorizationUrl,
        string Provider,
        string State
    );

    public sealed record TestConnectionResponseDto(
        bool Success,
        string Provider,
        string Message,
        int ResponseTimeMs
    );
}
