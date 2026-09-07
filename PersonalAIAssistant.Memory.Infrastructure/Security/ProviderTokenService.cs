using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    public class ProviderTokenService : IProviderTokenService
    {
        private readonly IProviderConnectionRepository _repository;
        private readonly IEncryptionService _encryptionService;
        private readonly ISecurityAuditLogger _auditLogger;
        private readonly IEnumerable<IOAuthProviderHandler> _oauthHandlers;
        private readonly ILogger<ProviderTokenService> _logger;
        private readonly string _systemKey;

        public ProviderTokenService(
            IProviderConnectionRepository repository,
            IEncryptionService encryptionService,
            ISecurityAuditLogger auditLogger,
            IEnumerable<IOAuthProviderHandler> oauthHandlers,
            IOptions<EncryptionOptions> encryptionOpts,
            ILogger<ProviderTokenService> logger)
        {
            _repository = repository;
            _encryptionService = encryptionService;
            _auditLogger = auditLogger;
            _oauthHandlers = oauthHandlers;
            _logger = logger;
            _systemKey = encryptionOpts.Value.SystemKey;
            if (string.IsNullOrWhiteSpace(_systemKey))
            {
                _systemKey = "DefaultSystemEncryptionKey32BytesLongSecret!";
            }
        }

        public string EncryptToken(string plainToken)
        {
            if (string.IsNullOrEmpty(plainToken)) return string.Empty;
            return _encryptionService.Encrypt(plainToken, _systemKey);
        }

        public string DecryptToken(string encryptedToken)
        {
            if (string.IsNullOrEmpty(encryptedToken)) return string.Empty;
            return _encryptionService.Decrypt(encryptedToken, _systemKey);
        }

        public async Task<bool> HasScopeConsentAsync(string userId, string provider, string requiredScope, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(provider))
                return false;

            var connection = await _repository.GetConnectionAsync(userId, provider, ct);
            if (connection == null || connection.Status != ProviderConnectionStatus.Active)
                return false;

            return CheckScope(connection.ConsentScopes, requiredScope);
        }

        public async Task<string?> GetValidAccessTokenAsync(string userId, string provider, string requiredScope, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(provider))
            {
                _auditLogger.LogProviderAccessAttempt(userId, provider, "GetValidAccessToken", "DENIED", "Missing user or provider parameter");
                return null;
            }

            var connection = await _repository.GetConnectionAsync(userId, provider, ct);
            if (connection == null)
            {
                _auditLogger.LogProviderAccessAttempt(userId, provider, "GetValidAccessToken", "DENIED", "No connection record exists");
                return null;
            }

            if (connection.Status == ProviderConnectionStatus.Revoked)
            {
                _auditLogger.LogProviderAccessAttempt(userId, provider, "GetValidAccessToken", "DENIED", "Connection is revoked");
                return null;
            }

            // Verify scope consent
            if (!string.IsNullOrWhiteSpace(requiredScope) && !CheckScope(connection.ConsentScopes, requiredScope))
            {
                _auditLogger.LogProviderAccessAttempt(userId, provider, "GetValidAccessToken", "DENIED", $"Missing required scope: {requiredScope}");
                return null;
            }

            // Check expiration
            if (connection.TokenExpiresAtUtc.HasValue && connection.TokenExpiresAtUtc.Value <= DateTime.UtcNow)
            {
                _logger.LogInformation("[ProviderTokenService] Access token for user {UserId} provider {Provider} is expired. Attempting refresh...",
                    userId, provider);

                if (!string.IsNullOrWhiteSpace(connection.EncryptedRefreshToken))
                {
                    var handler = _oauthHandlers.FirstOrDefault(h => h.ProviderName.Equals(provider, StringComparison.OrdinalIgnoreCase));
                    if (handler != null)
                    {
                        var plainRefreshToken = DecryptToken(connection.EncryptedRefreshToken);
                        var refreshResult = await handler.RefreshTokenAsync(plainRefreshToken, ct);

                        if (refreshResult != null && !string.IsNullOrWhiteSpace(refreshResult.AccessToken))
                        {
                            connection.EncryptedAccessToken = EncryptToken(refreshResult.AccessToken);
                            if (!string.IsNullOrWhiteSpace(refreshResult.RefreshToken))
                            {
                                connection.EncryptedRefreshToken = EncryptToken(refreshResult.RefreshToken);
                            }
                            connection.TokenExpiresAtUtc = refreshResult.ExpiresAtUtc;
                            connection.Status = ProviderConnectionStatus.Active;
                            connection.UpdatedAtUtc = DateTime.UtcNow;

                            await _repository.UpsertConnectionAsync(connection, ct);
                            _auditLogger.LogProviderTokenRefreshed(userId, provider);

                            await _repository.AddAuditLogAsync(new ProviderAuditLogEntity
                            {
                                UserId = userId,
                                Provider = provider,
                                EventType = "TOKEN_REFRESHED",
                                Details = $"Token automatically refreshed. New expiry: {refreshResult.ExpiresAtUtc}"
                            }, ct);

                            _auditLogger.LogProviderAccessAttempt(userId, provider, "GetValidAccessToken", "GRANTED");
                            return refreshResult.AccessToken;
                        }
                    }
                }

                // If refresh not possible or failed:
                connection.Status = ProviderConnectionStatus.Expired;
                await _repository.UpsertConnectionAsync(connection, ct);
                _auditLogger.LogProviderAccessAttempt(userId, provider, "GetValidAccessToken", "DENIED", "Access token expired and refresh failed");
                return null;
            }

            // Valid active token
            if (string.IsNullOrWhiteSpace(connection.EncryptedAccessToken))
            {
                _auditLogger.LogProviderAccessAttempt(userId, provider, "GetValidAccessToken", "DENIED", "No encrypted access token present");
                return null;
            }

            try
            {
                var decrypted = DecryptToken(connection.EncryptedAccessToken);
                if (string.IsNullOrWhiteSpace(decrypted))
                {
                    _auditLogger.LogProviderAccessAttempt(userId, provider, "GetValidAccessToken", "DENIED", "Failed to decrypt token");
                    return null;
                }

                _auditLogger.LogProviderAccessAttempt(userId, provider, "GetValidAccessToken", "GRANTED");
                return decrypted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProviderTokenService] Error decrypting access token for user {UserId} provider {Provider}", userId, provider);
                _auditLogger.LogProviderAccessAttempt(userId, provider, "GetValidAccessToken", "DENIED", $"Decryption exception: {ex.Message}");
                return null;
            }
        }

        private static bool CheckScope(string grantedScopes, string requiredScope)
        {
            if (string.IsNullOrWhiteSpace(grantedScopes)) return false;
            var scopes = grantedScopes.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            return scopes.Any(s =>
                s.Equals(requiredScope, StringComparison.OrdinalIgnoreCase) ||
                s.Equals("ai.admin", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("*", StringComparison.OrdinalIgnoreCase) ||
                (requiredScope.StartsWith("ai.") && s.Equals("ai.*", StringComparison.OrdinalIgnoreCase)));
        }
    }
}
