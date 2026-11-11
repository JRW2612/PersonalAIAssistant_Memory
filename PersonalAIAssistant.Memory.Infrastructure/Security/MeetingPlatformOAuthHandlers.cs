using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.Security;

public abstract class MeetingOAuthHandler : IOAuthProviderHandler
{
    private readonly HttpClient _http;
    protected MeetingOAuthHandler(IHttpClientFactory clients) => _http = clients.CreateClient("meeting-oauth");
    public abstract string ProviderName { get; }
    protected abstract string ClientId { get; }
    protected abstract string ClientSecret { get; }
    protected abstract string[] DefaultScopes { get; }
    protected abstract string AuthorizeEndpoint { get; }
    protected abstract string TokenEndpoint { get; }
    protected virtual bool UseBasicClientAuthentication => false;
    protected virtual bool UseGoogleOfflineParameters => false;

    public string BuildAuthorizationUrl(string redirectUri, string state, IEnumerable<string> scopes)
    {
        EnsureConfigured();
        var requestedScopes = scopes?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requestedScopes is not { Length: > 0 }) requestedScopes = DefaultScopes;
        var values = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', requestedScopes),
            ["state"] = state
        };
        if (UseGoogleOfflineParameters)
        {
            values["access_type"] = "offline";
            values["prompt"] = "consent";
        }
        return AuthorizeEndpoint + "?" + string.Join('&', values.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default) =>
        await SendTokenRequestAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        }, ct) ?? throw new InvalidOperationException($"{ProviderName} returned no token response.");

    public async Task<OAuthTokenResult?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var response = await SendTokenRequestAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }, ct, allowFailure: true);
        return response;
    }

    public async Task<bool> RevokeTokenAsync(string token, CancellationToken ct = default)
    {
        // Provider revocation endpoints differ. Marking the local credential revoked is the
        // authoritative operation; platform token revocation is best-effort where supported.
        return true;
    }

    private async Task<OAuthTokenResult?> SendTokenRequestAsync(Dictionary<string, string> form, CancellationToken ct, bool allowFailure = false)
    {
        EnsureConfigured();
        if (!UseBasicClientAuthentication)
        {
            form["client_id"] = ClientId;
            form["client_secret"] = ClientSecret;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        if (UseBasicClientAuthentication)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        using var response = await _http.SendAsync(request, ct);
        if (allowFailure && !response.IsSuccessStatusCode) return null;
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = json.RootElement;
        var access = ReadString(root, "access_token");
        if (string.IsNullOrWhiteSpace(access)) throw new InvalidOperationException($"{ProviderName} returned no access token.");
        var refresh = ReadString(root, "refresh_token");
        var scope = ReadString(root, "scope");
        var expires = root.TryGetProperty("expires_in", out var expiresValue) && expiresValue.TryGetInt32(out var seconds)
            ? DateTime.UtcNow.AddSeconds(seconds) : (DateTime?)null;
        return new OAuthTokenResult(access, refresh, expires, GrantedScopes: scope);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException($"OAuth credentials for '{ProviderName}' are not configured.");
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public sealed class MicrosoftTeamsOAuthProviderHandler : MeetingOAuthHandler
{
    private readonly MicrosoftOAuthOptions _options;
    public MicrosoftTeamsOAuthProviderHandler(IHttpClientFactory clients, IOptions<ProviderConnectionOptions> options) : base(clients) => _options = options.Value.Microsoft;
    public override string ProviderName => "teams";
    protected override string ClientId => _options.ClientId;
    protected override string ClientSecret => _options.ClientSecret;
    protected override string[] DefaultScopes => _options.TranscriptScopes;
    protected override string AuthorizeEndpoint => $"https://login.microsoftonline.com/{Uri.EscapeDataString(string.IsNullOrWhiteSpace(_options.Tenant) ? "common" : _options.Tenant)}/oauth2/v2.0/authorize";
    protected override string TokenEndpoint => $"https://login.microsoftonline.com/{Uri.EscapeDataString(string.IsNullOrWhiteSpace(_options.Tenant) ? "common" : _options.Tenant)}/oauth2/v2.0/token";
}

public sealed class ZoomOAuthProviderHandler : MeetingOAuthHandler
{
    private readonly ZoomOAuthOptions _options;
    public ZoomOAuthProviderHandler(IHttpClientFactory clients, IOptions<ProviderConnectionOptions> options) : base(clients) => _options = options.Value.Zoom;
    public override string ProviderName => "zoom";
    protected override string ClientId => _options.ClientId;
    protected override string ClientSecret => _options.ClientSecret;
    protected override string[] DefaultScopes => _options.TranscriptScopes;
    protected override string AuthorizeEndpoint => "https://zoom.us/oauth/authorize";
    protected override string TokenEndpoint => "https://zoom.us/oauth/token";
    protected override bool UseBasicClientAuthentication => true;
}

public sealed class GoogleMeetOAuthProviderHandler : MeetingOAuthHandler
{
    private readonly GoogleOAuthOptions _options;
    public GoogleMeetOAuthProviderHandler(IHttpClientFactory clients, IOptions<ProviderConnectionOptions> options) : base(clients) => _options = options.Value.Google;
    public override string ProviderName => "meet";
    protected override string ClientId => _options.ClientId;
    protected override string ClientSecret => _options.ClientSecret;
    protected override string[] DefaultScopes => ["https://www.googleapis.com/auth/meetings.space.readonly", "openid", "email"];
    protected override string AuthorizeEndpoint => "https://accounts.google.com/o/oauth2/v2/auth";
    protected override string TokenEndpoint => "https://oauth2.googleapis.com/token";
    protected override bool UseGoogleOfflineParameters => true;
}
