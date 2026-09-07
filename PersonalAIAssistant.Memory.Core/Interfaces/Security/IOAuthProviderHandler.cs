namespace PersonalAIAssistant.Memory.Core.Interfaces.Security;

public sealed record OAuthTokenResult(string AccessToken, string? RefreshToken, DateTime? ExpiresAtUtc, string? AccountEmail = null, string? AccountName = null);

public interface IOAuthProviderHandler
{
    string ProviderName { get; }
    string BuildAuthorizationUrl(string redirectUri, string state, IEnumerable<string> scopes);
    Task<OAuthTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);
    Task<OAuthTokenResult?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);
    Task<bool> RevokeTokenAsync(string token, CancellationToken ct = default);
}
