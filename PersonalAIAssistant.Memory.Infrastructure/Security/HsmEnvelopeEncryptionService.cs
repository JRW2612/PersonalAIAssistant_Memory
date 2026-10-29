using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    /// <summary>
    /// Enterprise FIPS 140-2 Level 3 Hardware Security Module (HSM) Envelope Encryption Service.
    /// Combines cloud HSM Master Key (KEK) protection with local AES-256-GCM AEAD encryption.
    /// Format: [Version 0x03 (1B)][WrappedDekLen (2B)][WrappedDek (NB)][Nonce (12B)][Tag (16B)][Ciphertext (NB)]
    /// Supports automatic cryptographic zeroization of ephemeral DEKs and backward-compatible fallback to legacy AES-GCM (0x02) and AES-CBC (0x01).
    /// </summary>
    public sealed class HsmEnvelopeEncryptionService : IEncryptionService
    {
        public const byte FormatVersion = 0x03;
        private const int DekSize = 32;          // 256-bit AES DEK
        private const int NonceSize = 12;        // Standard 96-bit AES-GCM nonce
        private const int TagSize = 16;          // 128-bit authentication tag

        private readonly IHsmKeyVaultService _hsmKeyVault;
        private readonly AesGcmEncryptionService _legacyFallback;
        private readonly HsmOptions _options;
        private readonly ILogger<HsmEnvelopeEncryptionService> _logger;

        public HsmEnvelopeEncryptionService(
            IHsmKeyVaultService hsmKeyVault,
            AesGcmEncryptionService legacyFallback,
            IOptions<HsmOptions> options,
            ILogger<HsmEnvelopeEncryptionService> logger)
        {
            _hsmKeyVault = hsmKeyVault;
            _legacyFallback = legacyFallback;
            _options = options.Value ?? new HsmOptions();
            _logger = logger;
        }

        public string Encrypt(string plainText, string key)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            var keyName = string.IsNullOrWhiteSpace(key) ? _options.MasterKeyName : key;

            // 1. Generate ephemeral single-use 256-bit Data Encryption Key (DEK)
            var dek = RandomNumberGenerator.GetBytes(DekSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = new byte[plainBytes.Length];

            try
            {
                // 2. Encrypt plaintext using AES-256-GCM AEAD
                using (var aesGcm = new AesGcm(dek, TagSize))
                {
                    aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);
                }

                // 3. Wrap DEK inside FIPS 140-2 Level 3 HSM using Master KEK
                var wrappedDek = _hsmKeyVault.WrapKeyAsync(keyName, dek).GetAwaiter().GetResult();

                // 4. Assemble Envelope binary payload:
                // [Version(1B)][WrappedDekLen(2B)][WrappedDek(NB)][Nonce(12B)][Tag(16B)][Ciphertext(NB)]
                var totalLen = 1 + 2 + wrappedDek.Length + NonceSize + TagSize + cipherBytes.Length;
                var output = new byte[totalLen];

                output[0] = FormatVersion;
                var lenBytes = BitConverter.GetBytes((ushort)wrappedDek.Length);
                Buffer.BlockCopy(lenBytes, 0, output, 1, 2);
                Buffer.BlockCopy(wrappedDek, 0, output, 3, wrappedDek.Length);
                Buffer.BlockCopy(nonce, 0, output, 3 + wrappedDek.Length, NonceSize);
                Buffer.BlockCopy(tag, 0, output, 3 + wrappedDek.Length + NonceSize, TagSize);
                Buffer.BlockCopy(cipherBytes, 0, output, 3 + wrappedDek.Length + NonceSize + TagSize, cipherBytes.Length);

                return Convert.ToBase64String(output);
            }
            finally
            {
                // 5. Cryptographically zeroize plaintext DEK from memory
                CryptographicOperations.ZeroMemory(dek);
            }
        }

        public string Decrypt(string cipherText, string key)
        {
            if (string.IsNullOrEmpty(cipherText)) return string.Empty;
            var keyName = string.IsNullOrWhiteSpace(key) ? _options.MasterKeyName : key;

            byte[] data;
            try
            {
                data = Convert.FromBase64String(cipherText);
            }
            catch
            {
                // Unrecognized format -> fallback to legacy
                return _legacyFallback.Decrypt(cipherText, key);
            }

            // Detect whether this is an HSM Envelope payload (starts with 0x03 and has sufficient length)
            if (data.Length < 35 || data[0] != FormatVersion)
            {
                _logger.LogDebug("[HSM Encryption] Non-envelope ciphertext detected. Delegating to legacy AES-GCM / CBC decryptor.");
                return _legacyFallback.Decrypt(cipherText, key);
            }

            var wrappedDekLen = BitConverter.ToUInt16(data, 1);
            if (data.Length < 3 + wrappedDekLen + NonceSize + TagSize)
            {
                _logger.LogWarning("[HSM Encryption] Ciphertext corrupted or truncated. Delegating to fallback.");
                return _legacyFallback.Decrypt(cipherText, key);
            }

            var wrappedDek = new byte[wrappedDekLen];
            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];

            Buffer.BlockCopy(data, 3, wrappedDek, 0, wrappedDekLen);
            Buffer.BlockCopy(data, 3 + wrappedDekLen, nonce, 0, NonceSize);
            Buffer.BlockCopy(data, 3 + wrappedDekLen + NonceSize, tag, 0, TagSize);

            var cipherLen = data.Length - (3 + wrappedDekLen + NonceSize + TagSize);
            var cipherBytes = new byte[cipherLen];
            Buffer.BlockCopy(data, 3 + wrappedDekLen + NonceSize + TagSize, cipherBytes, 0, cipherLen);

            // 1. Unwrap DEK inside HSM partition
            var dek = _hsmKeyVault.UnwrapKeyAsync(keyName, wrappedDek).GetAwaiter().GetResult();
            var plainBytes = new byte[cipherLen];

            try
            {
                // 2. Decrypt ciphertext payload using AES-256-GCM
                using var aesGcm = new AesGcm(dek, TagSize);
                aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
                return Encoding.UTF8.GetString(plainBytes);
            }
            finally
            {
                // 3. Cryptographically zeroize unwrapped DEK from memory
                CryptographicOperations.ZeroMemory(dek);
            }
        }
    }
}
