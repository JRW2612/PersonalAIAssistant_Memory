using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    public class WorkloadAuthenticationService : IWorkloadAuthenticationService
    {
        private readonly IWorkloadIdentityRepository _repo;
        private readonly IEncryptionService _encryptionService;
        private readonly ISecurityAuditLogger _auditLogger;
        private readonly ILogger<WorkloadAuthenticationService> _logger;
        private readonly string _systemKey;

        public WorkloadAuthenticationService(
            IWorkloadIdentityRepository repo,
            IEncryptionService encryptionService,
            ISecurityAuditLogger auditLogger,
            IOptions<EncryptionOptions> encryptionOpts,
            ILogger<WorkloadAuthenticationService> logger)
        {
            _repo = repo;
            _encryptionService = encryptionService;
            _auditLogger = auditLogger;
            _logger = logger;
            _systemKey = !string.IsNullOrWhiteSpace(encryptionOpts.Value.SystemKey)
                ? encryptionOpts.Value.SystemKey
                : "DefaultWorkloadAuthenticationSystemKey32B!";
        }

        public async Task<WorkloadIdentityEntity?> AuthenticateClientCredentialsAsync(
            string clientId, string clientSecret, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                _auditLogger.LogAccessDenied("anonymous", "M2M_AUTH", clientId, "Missing client credentials");
                return null;
            }

            var workload = await _repo.GetByClientIdAsync(clientId, ct);
            if (workload == null || workload.Status != WorkloadStatus.Active)
            {
                _auditLogger.LogAccessDenied(clientId, "M2M_AUTH", clientId, "Workload not found or inactive");
                return null;
            }

            if (string.IsNullOrWhiteSpace(workload.HashedSecret))
            {
                _auditLogger.LogAccessDenied(clientId, "M2M_AUTH", clientId, "Workload has no client secret configured");
                return null;
            }

            var inputHash = ComputeHash(clientSecret);
            var expectedBytes = Encoding.UTF8.GetBytes(workload.HashedSecret);
            var actualBytes = Encoding.UTF8.GetBytes(inputHash);

            if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
            {
                _auditLogger.LogAccessDenied(clientId, "M2M_AUTH", clientId, "Invalid client secret");
                return null;
            }

            await _repo.RecordUsageAsync(clientId, ct);
            _auditLogger.LogAccessGranted(clientId, "M2M_AUTH", clientId);
            return workload;
        }

        public async Task<WorkloadIdentityEntity?> AuthenticateOidcTokenAsync(
            string rawJwtToken, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rawJwtToken)) return null;

            try
            {
                var parts = rawJwtToken.Split('.');
                if (parts.Length < 2)
                {
                    _logger.LogWarning("[OIDC Auth] Incoming token is not a valid JWT format.");
                    return null;
                }

                var payloadBytes = Base64UrlDecode(parts[1]);
                var payloadJson = Encoding.UTF8.GetString(payloadBytes);

                using var doc = JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;

                var issuer = root.TryGetProperty("iss", out var issElem) ? issElem.GetString() : null;
                var subject = root.TryGetProperty("sub", out var subElem) ? subElem.GetString() : null;
                string? audience = null;

                if (root.TryGetProperty("aud", out var audElem))
                {
                    audience = audElem.ValueKind == JsonValueKind.Array
                        ? audElem.EnumerateArray().FirstOrDefault().GetString()
                        : audElem.GetString();
                }

                if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
                {
                    _logger.LogWarning("[OIDC Auth] JWT token missing issuer or subject claim.");
                    return null;
                }

                var matchedWorkload = await _repo.FindMatchingOidcWorkloadAsync(issuer, audience ?? string.Empty, subject, ct);
                if (matchedWorkload == null)
                {
                    _logger.LogWarning("[OIDC Auth] No registered workload matches Issuer='{Issuer}', Audience='{Audience}', Subject='{Subject}'.",
                        issuer, audience, subject);
                    _auditLogger.LogAccessDenied(subject, "OIDC_AUTH", issuer, "No matching trusted workload identity");
                    return null;
                }

                await _repo.RecordUsageAsync(matchedWorkload.ClientId, ct);
                _auditLogger.LogAccessGranted(matchedWorkload.ClientId, "OIDC_AUTH", subject);
                return matchedWorkload;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OIDC Auth] Failed to parse or evaluate incoming OIDC token.");
                return null;
            }
        }

        public async Task<bool> VerifyWebhookSignatureAsync(
            string clientId, string rawBody, string signatureHeader, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(signatureHeader))
                return false;

            var workload = await _repo.GetByClientIdAsync(clientId, ct);
            if (workload == null || workload.Status != WorkloadStatus.Active)
                return false;

            if (string.IsNullOrWhiteSpace(workload.EncryptedWebhookSecret))
                return false;

            try
            {
                var secret = _encryptionService.Decrypt(workload.EncryptedWebhookSecret, _systemKey);
                var expectedSignature = ComputeHmacSha256(rawBody, secret);

                // Handle GitHub-style "sha256=..." prefix
                var cleanSignature = signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
                    ? signatureHeader["sha256=".Length..]
                    : signatureHeader;

                var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature.ToLowerInvariant());
                var actualBytes = Encoding.UTF8.GetBytes(cleanSignature.ToLowerInvariant());

                if (expectedBytes.Length != actualBytes.Length) return false;

                var valid = CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
                if (valid)
                {
                    await _repo.RecordUsageAsync(clientId, ct);
                    _auditLogger.LogAccessGranted(clientId, "WEBHOOK_VERIFY", clientId);
                }
                else
                {
                    _auditLogger.LogAccessDenied(clientId, "WEBHOOK_VERIFY", clientId, "Signature mismatch");
                }

                return valid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Webhook Verify] Error verifying signature for workload '{ClientId}'.", clientId);
                return false;
            }
        }

        private static string ComputeHash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string ComputeHmacSha256(string payload, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
