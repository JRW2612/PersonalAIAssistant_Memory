using Microsoft.AspNetCore.Mvc;
using PersonalAIAssistant.Memory.Api.DTOs;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using System.Diagnostics;

namespace PersonalAIAssistant.Memory.Api.Controllers
{
    [ApiController]
    [Route("api/v1/connections")]
    public class ProviderConnectionsController : ControllerBase
    {
        private readonly IProviderConnectionRepository _repo;
        private readonly IProviderTokenService _tokenService;
        private readonly IOAuthStateManager _stateManager;
        private readonly IEnumerable<IOAuthProviderHandler> _oauthHandlers;
        private readonly IUserContext _userContext;
        private readonly ISecurityAuditLogger _auditLogger;
        private readonly IAIProviderFactory _aiFactory;
        private readonly ILogger<ProviderConnectionsController> _logger;

        public ProviderConnectionsController(
            IProviderConnectionRepository repo,
            IProviderTokenService tokenService,
            IOAuthStateManager stateManager,
            IEnumerable<IOAuthProviderHandler> oauthHandlers,
            IUserContext userContext,
            ISecurityAuditLogger auditLogger,
            IAIProviderFactory aiFactory,
            ILogger<ProviderConnectionsController> logger)
        {
            _repo = repo;
            _tokenService = tokenService;
            _stateManager = stateManager;
            _oauthHandlers = oauthHandlers;
            _userContext = userContext;
            _auditLogger = auditLogger;
            _aiFactory = aiFactory;
            _logger = logger;
        }

        private string ResolveUserId()
        {
            if (!string.IsNullOrWhiteSpace(_userContext.UserId))
                return _userContext.UserId;

            if (Request.Headers.TryGetValue("X-User-Id", out var customUser) && !string.IsNullOrWhiteSpace(customUser))
                return customUser.ToString();

            return "user-alice"; // Default fallback identity for demo/testing
        }

        private string ResolveTenantId()
        {
            return !string.IsNullOrWhiteSpace(_userContext.TenantId) ? _userContext.TenantId : "default";
        }

        /// <summary>
        /// Lists all connected and available AI providers for the current user.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ProviderConnectionDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetConnections(CancellationToken ct)
        {
            var userId = ResolveUserId();
            var connections = await _repo.GetUserConnectionsAsync(userId, ct);

            var availableProviders = new[] { "gemini", "openai", "anthropic" };
            var results = new List<ProviderConnectionDto>();

            foreach (var p in availableProviders)
            {
                var conn = connections.FirstOrDefault(c => c.Provider.Equals(p, StringComparison.OrdinalIgnoreCase));
                if (conn != null)
                {
                    results.Add(MapToDto(conn));
                }
                else
                {
                    results.Add(new ProviderConnectionDto(
                        Provider: p,
                        Status: "NotConnected",
                        CredentialKind: "None",
                        ConsentScopes: Array.Empty<string>(),
                        TokenExpiresAtUtc: null,
                        AccountEmail: null,
                        AccountName: null,
                        CreatedAtUtc: DateTime.MinValue,
                        UpdatedAtUtc: DateTime.MinValue,
                        IsActive: false
                    ));
                }
            }

            return Ok(results);
        }

        /// <summary>
        /// Retrieves connection details for a specific provider.
        /// </summary>
        [HttpGet("{provider}")]
        [ProducesResponseType(typeof(ProviderConnectionDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetConnection(string provider, CancellationToken ct)
        {
            var userId = ResolveUserId();
            var conn = await _repo.GetConnectionAsync(userId, provider, ct);
            if (conn == null) return NotFound($"No connection found for provider '{provider}'.");

            return Ok(MapToDto(conn));
        }

        /// <summary>
        /// Initiates OAuth 2.0 flow for a provider, generating a signed anti-CSRF state token and auth URL.
        /// </summary>
        [HttpGet("{provider}/authorize")]
        [ProducesResponseType(typeof(AuthorizeResponseDto), StatusCodes.Status200OK)]
        public IActionResult Authorize(
            string provider,
            [FromQuery] string? redirectUri = null,
            [FromQuery] string? scopes = null,
            [FromQuery] bool redirect = false)
        {
            var userId = ResolveUserId();
            var tenantId = ResolveTenantId();
            var handler = _oauthHandlers.FirstOrDefault(h => h.ProviderName.Equals(provider, StringComparison.OrdinalIgnoreCase));

            if (handler == null)
            {
                return BadRequest($"OAuth 2.0 is not supported for provider '{provider}'.");
            }

            var callbackUrl = redirectUri;
            if (string.IsNullOrWhiteSpace(callbackUrl))
            {
                callbackUrl = $"{Request.Scheme}://{Request.Host}/api/v1/connections/{provider.ToLowerInvariant()}/callback";
            }

            var scopeList = !string.IsNullOrWhiteSpace(scopes)
                ? scopes.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                : new[] { "ai.generate", "ai.memory.read", "ai.memory.write" };

            var state = _stateManager.Create(userId, tenantId, provider, scopeList);
            var authUrl = handler.BuildAuthorizationUrl(callbackUrl, state, scopeList);

            if (redirect)
            {
                return Redirect(authUrl);
            }

            return Ok(new AuthorizeResponseDto(
                AuthorizationUrl: authUrl,
                Provider: provider.ToLowerInvariant(),
                State: state
            ));
        }

        /// <summary>
        /// Handles OAuth 2.0 authorization callback from provider. Exchanges authorization code for tokens,
        /// encrypts credentials with AES-256-GCM, stores them, and logs an audit record.
        /// </summary>
        [HttpGet("{provider}/callback")]
        public async Task<IActionResult> Callback(
            string provider,
            [FromQuery] string? code = null,
            [FromQuery] string? state = null,
            [FromQuery] string? error = null,
            CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning("[OAuth Callback] Provider '{Provider}' returned error: {Error}", provider, error);
                return Redirect($"/connections?status=error&message={Uri.EscapeDataString(error)}");
            }

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            {
                return BadRequest("Missing code or state parameter.");
            }

            OAuthState oAuthState;
            try
            {
                oAuthState = _stateManager.Consume(state);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[OAuth Callback] Anti-CSRF state validation failed for provider '{Provider}'.", provider);
                return BadRequest($"Invalid or expired state parameter: {ex.Message}");
            }

            var handler = _oauthHandlers.FirstOrDefault(h => h.ProviderName.Equals(provider, StringComparison.OrdinalIgnoreCase));
            if (handler == null)
            {
                return BadRequest($"No OAuth handler configured for provider '{provider}'.");
            }

            var redirectUri = $"{Request.Scheme}://{Request.Host}/api/v1/connections/{provider.ToLowerInvariant()}/callback";

            try
            {
                var tokenResult = await handler.ExchangeCodeAsync(code, redirectUri, ct);

                var grantedScopes = string.Join(" ", oAuthState.Scopes);

                var connection = new ProviderConnectionEntity
                {
                    UserId = oAuthState.UserId,
                    TenantId = oAuthState.TenantId,
                    Provider = provider.ToLowerInvariant(),
                    EncryptedAccessToken = _tokenService.EncryptToken(tokenResult.AccessToken),
                    EncryptedRefreshToken = !string.IsNullOrWhiteSpace(tokenResult.RefreshToken)
                        ? _tokenService.EncryptToken(tokenResult.RefreshToken)
                        : null,
                    TokenExpiresAtUtc = tokenResult.ExpiresAtUtc,
                    ConsentScopes = grantedScopes,
                    Status = ProviderConnectionStatus.Active,
                    CredentialKind = ProviderCredentialKind.OAuthBearerToken,
                    AccountEmail = tokenResult.AccountEmail,
                    AccountName = tokenResult.AccountName
                };

                await _repo.UpsertConnectionAsync(connection, ct);

                _auditLogger.LogProviderConnected(oAuthState.UserId, provider, grantedScopes);

                await _repo.AddAuditLogAsync(new ProviderAuditLogEntity
                {
                    UserId = oAuthState.UserId,
                    TenantId = oAuthState.TenantId,
                    Provider = provider.ToLowerInvariant(),
                    EventType = "CONNECTED",
                    Details = $"Successfully connected via OAuth 2.0. Granted scopes: {grantedScopes}",
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers.UserAgent.ToString()
                }, ct);

                _logger.LogInformation("[OAuth Callback] Successfully connected user '{UserId}' to provider '{Provider}'.",
                    oAuthState.UserId, provider);

                return Redirect($"/connections?status=connected&provider={provider.ToLowerInvariant()}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OAuth Callback] Error exchanging code for provider '{Provider}'.", provider);
                return Redirect($"/connections?status=error&message={Uri.EscapeDataString(ex.Message)}");
            }
        }

        /// <summary>
        /// Direct connection endpoint for users connecting via API Key or pre-acquired bearer token (BYOK).
        /// </summary>
        [HttpPost("{provider}/connect")]
        [ProducesResponseType(typeof(ProviderConnectionDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> ConnectDirect(
            string provider,
            [FromBody] DirectConnectRequestDto request,
            CancellationToken ct)
        {
            var userId = ResolveUserId();
            var tenantId = ResolveTenantId();

            var token = !string.IsNullOrWhiteSpace(request.ApiKey) ? request.ApiKey : request.AccessToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                return BadRequest("Either ApiKey or AccessToken is required.");
            }

            var scopes = request.Scopes != null && request.Scopes.Any()
                ? string.Join(" ", request.Scopes)
                : "ai.generate ai.memory.read ai.memory.write";

            var kind = !string.IsNullOrWhiteSpace(request.ApiKey)
                ? ProviderCredentialKind.ApiKey
                : ProviderCredentialKind.OAuthBearerToken;

            var connection = new ProviderConnectionEntity
            {
                UserId = userId,
                TenantId = tenantId,
                Provider = provider.ToLowerInvariant(),
                EncryptedAccessToken = _tokenService.EncryptToken(token),
                EncryptedRefreshToken = !string.IsNullOrWhiteSpace(request.RefreshToken)
                    ? _tokenService.EncryptToken(request.RefreshToken)
                    : null,
                TokenExpiresAtUtc = request.ExpiresAtUtc,
                ConsentScopes = scopes,
                Status = ProviderConnectionStatus.Active,
                CredentialKind = kind,
                AccountEmail = request.AccountEmail,
                AccountName = request.AccountName
            };

            await _repo.UpsertConnectionAsync(connection, ct);

            _auditLogger.LogProviderConnected(userId, provider, scopes);

            await _repo.AddAuditLogAsync(new ProviderAuditLogEntity
            {
                UserId = userId,
                TenantId = tenantId,
                Provider = provider.ToLowerInvariant(),
                EventType = "CONNECTED",
                Details = $"Connected via Direct BYOK ({kind}). Scopes: {scopes}",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()
            }, ct);

            return Ok(MapToDto(connection));
        }

        /// <summary>
        /// Revokes and disconnects an AI provider, purging stored encrypted credentials.
        /// </summary>
        [HttpDelete("{provider}/revoke")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Revoke(
            string provider,
            [FromBody] RevokeConnectionRequestDto? request,
            CancellationToken ct)
        {
            var userId = ResolveUserId();
            var tenantId = ResolveTenantId();
            var reason = request?.Reason ?? "User requested disconnection";

            var existing = await _repo.GetConnectionAsync(userId, provider, ct);
            if (existing != null && !string.IsNullOrWhiteSpace(existing.EncryptedAccessToken))
            {
                var handler = _oauthHandlers.FirstOrDefault(h => h.ProviderName.Equals(provider, StringComparison.OrdinalIgnoreCase));
                if (handler != null)
                {
                    try
                    {
                        var plainToken = _tokenService.DecryptToken(existing.EncryptedAccessToken);
                        await handler.RevokeTokenAsync(plainToken, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Revoke] Remote token revocation failed for provider '{Provider}'.", provider);
                    }
                }
            }

            await _repo.RevokeConnectionAsync(userId, provider, reason, ct);

            _auditLogger.LogProviderRevoked(userId, provider, reason);

            await _repo.AddAuditLogAsync(new ProviderAuditLogEntity
            {
                UserId = userId,
                TenantId = tenantId,
                Provider = provider.ToLowerInvariant(),
                EventType = "REVOKED",
                Details = $"Connection revoked. Reason: {reason}",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()
            }, ct);

            return NoContent();
        }

        /// <summary>
        /// Retrieves tamper-evident audit logs for provider connections.
        /// </summary>
        [HttpGet("audit-logs")]
        [ProducesResponseType(typeof(IReadOnlyList<ProviderAuditLogDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAuditLogs(
            [FromQuery] string? provider = null,
            [FromQuery] int limit = 50,
            CancellationToken ct = default)
        {
            var userId = ResolveUserId();
            var logs = await _repo.GetAuditLogsAsync(userId, provider, limit, ct);

            var dtos = logs.Select(l => new ProviderAuditLogDto(
                Id: l.Id,
                Provider: l.Provider,
                EventType: l.EventType,
                Details: l.Details,
                IpAddress: l.IpAddress,
                TimestampUtc: l.TimestampUtc
            )).ToList();

            return Ok(dtos);
        }

        /// <summary>
        /// Tests connectivity with the connected AI provider using the stored encrypted credentials.
        /// </summary>
        [HttpPost("{provider}/test")]
        [ProducesResponseType(typeof(TestConnectionResponseDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> TestConnection(string provider, CancellationToken ct)
        {
            var userId = ResolveUserId();
            var sw = Stopwatch.StartNew();

            try
            {
                var token = await _tokenService.GetValidAccessTokenAsync(userId, provider, "ai.generate", ct);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return Ok(new TestConnectionResponseDto(
                        Success: false,
                        Provider: provider,
                        Message: "No valid active token found or scope 'ai.generate' was not granted.",
                        ResponseTimeMs: (int)sw.ElapsedMilliseconds
                    ));
                }

                var aiProvider = _aiFactory.GetProvider(provider);
                var pingResponse = await aiProvider.GetResponseAsync("Ping. Respond with 'Connected'.", ct);
                sw.Stop();

                return Ok(new TestConnectionResponseDto(
                    Success: true,
                    Provider: provider,
                    Message: $"Live connectivity confirmed! Provider response: {pingResponse.Trim()}",
                    ResponseTimeMs: (int)sw.ElapsedMilliseconds
                ));
            }
            catch (Exception ex)
            {
                sw.Stop();
                return Ok(new TestConnectionResponseDto(
                    Success: false,
                    Provider: provider,
                    Message: $"Connection test failed: {ex.Message}",
                    ResponseTimeMs: (int)sw.ElapsedMilliseconds
                ));
            }
        }

        private static ProviderConnectionDto MapToDto(ProviderConnectionEntity entity)
        {
            var scopes = !string.IsNullOrWhiteSpace(entity.ConsentScopes)
                ? entity.ConsentScopes.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                : Array.Empty<string>();

            return new ProviderConnectionDto(
                Provider: entity.Provider,
                Status: entity.Status.ToString(),
                CredentialKind: entity.CredentialKind.ToString(),
                ConsentScopes: scopes,
                TokenExpiresAtUtc: entity.TokenExpiresAtUtc,
                AccountEmail: entity.AccountEmail,
                AccountName: entity.AccountName,
                CreatedAtUtc: entity.CreatedAtUtc,
                UpdatedAtUtc: entity.UpdatedAtUtc,
                IsActive: entity.Status == ProviderConnectionStatus.Active
            );
        }
    }
}
