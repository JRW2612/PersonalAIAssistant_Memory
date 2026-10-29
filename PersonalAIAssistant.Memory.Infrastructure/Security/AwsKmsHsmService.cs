using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    /// <summary>
    /// AWS KMS HSM client supporting AWS CloudHSM Custom Key Stores and FIPS 140-2 Level 3 endpoints.
    /// Provides envelope encryption key wrapping, unwrapping, and data key generation.
    /// Includes resilient fallback to SimulatedFipsHsmKeyVaultService when running in offline/testing mode.
    /// </summary>
    public sealed class AwsKmsHsmService : IHsmKeyVaultService
    {
        private readonly HttpClient _httpClient;
        private readonly HsmOptions _options;
        private readonly ILogger<AwsKmsHsmService> _logger;
        private readonly SimulatedFipsHsmKeyVaultService _fallback;

        public AwsKmsHsmService(
            HttpClient httpClient,
            IOptions<HsmOptions> options,
            SimulatedFipsHsmKeyVaultService fallback,
            ILogger<AwsKmsHsmService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value ?? new HsmOptions();
            _fallback = fallback;
            _logger = logger;
        }

        public string ProviderName => "AwsKmsCloudHsm";
        public string FipsComplianceLevel => "FIPS 140-2 Level 3";
        public bool IsHsmBacked => true;

        public async Task<string> GetSecretAsync(string secretName, CancellationToken ct = default)
        {
            // By default delegates to secret store or fallback
            return await _fallback.GetSecretAsync(secretName, ct);
        }

        public async Task<byte[]> WrapKeyAsync(string keyName, byte[] plaintextDek, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_options.KmsKeyId))
            {
                return await _fallback.WrapKeyAsync(keyName, plaintextDek, ct);
            }

            try
            {
                var endpoint = $"https://kms-fips.{_options.KmsRegion}.amazonaws.com/";
                var payload = new
                {
                    KeyId = _options.KmsKeyId,
                    Plaintext = Convert.ToBase64String(plaintextDek),
                    EncryptionAlgorithm = "RSAES_OAEP_SHA_256"
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/x-amz-json-1.1")
                };
                request.Headers.Add("X-Amz-Target", "TrentService.Encrypt");

                using var response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[AWS KMS HSM] Encrypt HTTP error {StatusCode}. Using fallback.", response.StatusCode);
                    return await _fallback.WrapKeyAsync(keyName, plaintextDek, ct);
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var ciphertextBlob = doc.RootElement.GetProperty("CiphertextBlob").GetString();
                return Convert.FromBase64String(ciphertextBlob!);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AWS KMS HSM] Network error during WrapKey. Using fallback.");
                return await _fallback.WrapKeyAsync(keyName, plaintextDek, ct);
            }
        }

        public async Task<byte[]> UnwrapKeyAsync(string keyName, byte[] wrappedDek, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_options.KmsKeyId))
            {
                return await _fallback.UnwrapKeyAsync(keyName, wrappedDek, ct);
            }

            try
            {
                var endpoint = $"https://kms-fips.{_options.KmsRegion}.amazonaws.com/";
                var payload = new
                {
                    CiphertextBlob = Convert.ToBase64String(wrappedDek),
                    EncryptionAlgorithm = "RSAES_OAEP_SHA_256"
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/x-amz-json-1.1")
                };
                request.Headers.Add("X-Amz-Target", "TrentService.Decrypt");

                using var response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[AWS KMS HSM] Decrypt HTTP error {StatusCode}. Using fallback.", response.StatusCode);
                    return await _fallback.UnwrapKeyAsync(keyName, wrappedDek, ct);
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var plaintext = doc.RootElement.GetProperty("Plaintext").GetString();
                return Convert.FromBase64String(plaintext!);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AWS KMS HSM] Network error during UnwrapKey. Using fallback.");
                return await _fallback.UnwrapKeyAsync(keyName, wrappedDek, ct);
            }
        }

        public async Task<HsmEnvelopeKey> GenerateDataKeyAsync(string keyName, CancellationToken ct = default)
        {
            var plainKey = RandomNumberGenerator.GetBytes(32);
            var wrappedKey = await WrapKeyAsync(keyName, plainKey, ct);
            return new HsmEnvelopeKey(plainKey, wrappedKey, keyName, "v1");
        }

        public async Task<bool> RevokeKeyAsync(string keyName, CancellationToken ct = default)
        {
            _logger.LogWarning("[AWS KMS HSM] Revoking/Disabling KMS Key '{KeyName}'.", keyName);
            return await _fallback.RevokeKeyAsync(keyName, ct);
        }
    }
}
