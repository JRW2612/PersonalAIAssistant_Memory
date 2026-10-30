using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Models.Security;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class HsmEnvelopeEncryptionTests
    {
        private readonly SimulatedFipsHsmKeyVaultService _hsmService;
        private readonly AesEncryptionService _legacyCbc;
        private readonly AesGcmEncryptionService _legacyGcm;
        private readonly HsmEnvelopeEncryptionService _envelopeService;
        private readonly HsmOptions _hsmOptions;

        public HsmEnvelopeEncryptionTests()
        {
            _hsmOptions = new HsmOptions
            {
                Provider = "SimulatedHsm",
                FipsComplianceLevel = "FIPS 140-2 Level 3",
                MasterKeyName = "BankMasterKek_HDFC",
                JwtSecretKeyName = "JwtSigningSecret"
            };

            _hsmService = new SimulatedFipsHsmKeyVaultService(
                Options.Create(_hsmOptions),
                NullLogger<SimulatedFipsHsmKeyVaultService>.Instance);

            _legacyCbc = new AesEncryptionService();
            _legacyGcm = new AesGcmEncryptionService(NullLogger<AesGcmEncryptionService>.Instance, _legacyCbc);

            _envelopeService = new HsmEnvelopeEncryptionService(
                _hsmService,
                _legacyGcm,
                Options.Create(_hsmOptions),
                NullLogger<HsmEnvelopeEncryptionService>.Instance);
        }

        [Fact]
        public async Task Hsm_Stores_And_Retrieves_Jwt_Signing_Secret()
        {
            var secret = await _hsmService.GetSecretAsync("JwtSigningSecret");
            // SEC-02: SimulatedFipsHsmKeyVaultService no longer seeds a hardcoded literal.
            // Assert that a non-empty, sufficiently long cryptographically-random secret is returned.
            Assert.False(string.IsNullOrWhiteSpace(secret));
            Assert.True(secret.Length >= 32, "JWT signing secret must be at least 32 characters long.");
            Assert.DoesNotContain("EnterpriseBfsiFipsHsm", secret, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Envelope_Encryption_RoundTrip_Encrypts_And_Decrypts_Correctly()
        {
            var rawData = "Confidential Banking Memory: Customer savings balance INR 45,00,000 in Mumbai branch.";
            var keyName = "tenant-hdfc-kek";

            var encrypted = _envelopeService.Encrypt(rawData, keyName);
            Assert.False(string.IsNullOrWhiteSpace(encrypted));
            Assert.NotEqual(rawData, encrypted);

            // Verify binary format: First byte must be 0x03 (HSM Envelope AES-256-GCM)
            var bytes = Convert.FromBase64String(encrypted);
            Assert.Equal(0x03, bytes[0]);

            var decrypted = _envelopeService.Decrypt(encrypted, keyName);
            Assert.Equal(rawData, decrypted);
        }

        [Fact]
        public void Envelope_Encryption_Decrypts_Legacy_AesGcm_0x02_Ciphertext_Transparently()
        {
            var rawText = "Legacy AES-GCM data written prior to HSM upgrade.";
            var key = "legacy-encryption-key-for-gcm";

            // Encrypt using legacy 0x02 service
            var legacyCipher = _legacyGcm.Encrypt(rawText, key);
            var bytes = Convert.FromBase64String(legacyCipher);
            Assert.Equal(0x02, bytes[0]);

            // Decrypt using new HSM Envelope service
            var decrypted = _envelopeService.Decrypt(legacyCipher, key);
            Assert.Equal(rawText, decrypted);
        }

        [Fact]
        public void Envelope_Encryption_Decrypts_Legacy_AesCbc_0x01_Ciphertext_Transparently()
        {
            var rawText = "Very old AES-CBC payload.";
            var key = "legacy-cbc-key-for-fallback";

            var legacyCipher = _legacyCbc.Encrypt(rawText, key);
            var decrypted = _envelopeService.Decrypt(legacyCipher, key);
            Assert.Equal(rawText, decrypted);
        }

        [Fact]
        public async Task Cryptographic_Shredding_Prevents_Decryption_When_MasterKey_Revoked()
        {
            var sensitiveMemory = "Top Secret Financial Transaction ID: TXN-998877665544";
            var tenantKey = "tenant-sbi-kek";

            var encrypted = _envelopeService.Encrypt(sensitiveMemory, tenantKey);
            var decryptedBeforeRevoke = _envelopeService.Decrypt(encrypted, tenantKey);
            Assert.Equal(sensitiveMemory, decryptedBeforeRevoke);

            // Execute Cryptographic Shredding
            await _hsmService.RevokeKeyAsync(tenantKey);

            // Decryption must now throw CryptographicException
            Assert.Throws<CryptographicException>(() => _envelopeService.Decrypt(encrypted, tenantKey));
        }

        [Fact]
        public void Hsm_Reports_Fips_140_2_Level_3_Compliance()
        {
            Assert.Equal("FIPS 140-2 Level 3", _hsmService.FipsComplianceLevel);
            Assert.True(_hsmService.IsHsmBacked);
        }
    }
}
