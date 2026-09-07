using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    public sealed class OAuthStateManager : IOAuthStateManager
    {
        private readonly byte[] _hmacKey;
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

        public OAuthStateManager(IOptions<EncryptionOptions> encryptionOpts)
        {
            var keyStr = encryptionOpts.Value.SystemKey;
            if (string.IsNullOrWhiteSpace(keyStr))
            {
                keyStr = "DefaultOAuthStateHmacSecretKey32BytesLong!";
            }
            _hmacKey = Encoding.UTF8.GetBytes(keyStr);
        }

        public OAuthStateManager(string secretKey)
        {
            if (string.IsNullOrWhiteSpace(secretKey))
                throw new ArgumentNullException(nameof(secretKey));
            _hmacKey = Encoding.UTF8.GetBytes(secretKey);
        }

        private sealed record InternalStatePayload(
            string UserId,
            string TenantId,
            string Provider,
            List<string> Scopes,
            DateTime ExpiresAtUtc,
            string Nonce
        );

        public string Create(string userId, string tenantId, string provider, IEnumerable<string> scopes)
        {
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
            if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentNullException(nameof(provider));

            var payload = new InternalStatePayload(
                UserId: userId,
                TenantId: string.IsNullOrWhiteSpace(tenantId) ? "default" : tenantId,
                Provider: provider.Trim().ToLowerInvariant(),
                Scopes: scopes?.ToList() ?? new List<string>(),
                ExpiresAtUtc: DateTime.UtcNow.Add(DefaultTtl),
                Nonce: Guid.NewGuid().ToString("N")
            );

            var json = JsonSerializer.Serialize(payload);
            var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
            var signature = ComputeSignature(payloadB64);

            return $"{payloadB64}.{signature}";
        }

        public OAuthState Consume(string state)
        {
            if (string.IsNullOrWhiteSpace(state))
                throw new ArgumentException("State parameter cannot be empty.", nameof(state));

            var parts = state.Split('.');
            if (parts.Length != 2)
                throw new InvalidOperationException("OAuth state token is malformed.");

            var payloadB64 = parts[0];
            var signature = parts[1];

            var expectedSignature = ComputeSignature(payloadB64);
            var sigBytes = Encoding.UTF8.GetBytes(signature);
            var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);

            if (!CryptographicOperations.FixedTimeEquals(sigBytes, expectedBytes))
            {
                throw new System.Security.SecurityException("Invalid OAuth state signature. Possible tampering or CSRF attempt.");
            }

            InternalStatePayload? payload;
            try
            {
                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(payloadB64));
                payload = JsonSerializer.Deserialize<InternalStatePayload>(payloadJson);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to deserialize OAuth state payload: {ex.Message}", ex);
            }

            if (payload == null)
                throw new InvalidOperationException("OAuth state payload is null.");

            if (DateTime.UtcNow > payload.ExpiresAtUtc)
            {
                throw new InvalidOperationException("OAuth state token has expired. Please initiate authorization again.");
            }

            return new OAuthState(
                UserId: payload.UserId,
                TenantId: payload.TenantId,
                Provider: payload.Provider,
                Scopes: payload.Scopes.AsReadOnly(),
                ExpiresAtUtc: payload.ExpiresAtUtc
            );
        }

        private string ComputeSignature(string data)
        {
            using var hmac = new HMACSHA256(_hmacKey);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Base64UrlEncode(hash);
        }

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        private static byte[] Base64UrlDecode(string input)
        {
            var output = input.Replace("-", "+").Replace("_", "/");
            switch (output.Length % 4)
            {
                case 2: output += "=="; break;
                case 3: output += "="; break;
            }
            return Convert.FromBase64String(output);
        }
    }
}
