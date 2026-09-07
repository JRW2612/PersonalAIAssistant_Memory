using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.Security;

/// <summary>Google OAuth authorization-code handler. Simulation is opt-in and must never be enabled in production.</summary>
public sealed class GoogleOAuthProviderHandler : IOAuthProviderHandler
{
    public string ProviderName => "gemini";
    private readonly HttpClient _http;
    private readonly ProviderConnectionOptions _options;

    public GoogleOAuthProviderHandler(IHttpClientFactory factory, IOptions<ProviderConnectionOptions> options)
    {
        _http = factory.CreateClient("google-oauth");
        _options = options.Value;
    }

    public string BuildAuthorizationUrl(string redirectUri, string state, IEnumerable<string> scopes)
    {
        if (UseSimulation()) return $"{redirectUri}?code=simulated-{Guid.NewGuid():N}&state={Uri.EscapeDataString(state)}";
        EnsureConfigured();
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.Google.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', _options.Google.OAuthScopes),
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };
        return "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join('&', query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        if (UseSimulation()) return new OAuthTokenResult(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), Convert.ToHexString(RandomNumberGenerator.GetBytes(48)), DateTime.UtcNow.AddHours(1), "simulation@example.invalid", "OAuth Simulation");
        EnsureConfigured();
        var response = await _http.PostAsync("token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _options.Google.ClientId,
            ["client_secret"] = _options.Google.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        }), ct);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct) ?? throw new InvalidOperationException("Google returned an empty token response.");
        return new OAuthTokenResult(token.AccessToken, token.RefreshToken, DateTime.UtcNow.AddSeconds(token.ExpiresIn));
    }

    public async Task<OAuthTokenResult?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        if (UseSimulation()) return new OAuthTokenResult(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), refreshToken, DateTime.UtcNow.AddHours(1));
        EnsureConfigured();
        var response = await _http.PostAsync("token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = _options.Google.ClientId,
            ["client_secret"] = _options.Google.ClientSecret,
            ["grant_type"] = "refresh_token"
        }), ct);
        if (!response.IsSuccessStatusCode) return null;
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        return token is null ? null : new OAuthTokenResult(token.AccessToken, token.RefreshToken ?? refreshToken, DateTime.UtcNow.AddSeconds(token.ExpiresIn));
    }

    public async Task<bool> RevokeTokenAsync(string token, CancellationToken ct = default)
    {
        if (UseSimulation()) return true;
        var response = await _http.PostAsync("revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }), ct);
        return response.IsSuccessStatusCode;
    }

    private bool UseSimulation() => _options.EnableOAuthSimulation &&
        (string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase) ||
         string.IsNullOrWhiteSpace(_options.Google.ClientId));
    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.Google.ClientId) || string.IsNullOrWhiteSpace(_options.Google.ClientSecret))
            throw new InvalidOperationException("Google OAuth client credentials are not configured.");
    }
    private sealed class TokenResponse { public string AccessToken { get; set; } = string.Empty; public string? RefreshToken { get; set; } public int ExpiresIn { get; set; } }
}
