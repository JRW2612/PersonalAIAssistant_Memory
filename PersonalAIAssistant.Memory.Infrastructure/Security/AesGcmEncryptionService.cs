using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using System.Security.Cryptography;
using System.Text;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    /// <summary>
    /// AES-256-GCM encryption service (AEAD) with PBKDF2 key derivation.
    /// Provides authenticated encryption preventing ciphertext tampering (padding oracle attacks).
    /// Includes backward-compatible fallback to decrypt legacy AES-CBC data transparently.
    /// SRP: handles only encryption/decryption. OCP: cipher selection is version-tagged in binary format.
    /// Format: [Version(1)][Salt(16)][Nonce(12)][Tag(16)][Ciphertext(N)]
    /// </summary>
    public sealed class AesGcmEncryptionService : IEncryptionService
    {
        private const byte FormatVersion = 0x02; // 0x01 = legacy CBC (for detection only)
        private const int SaltSize = 16;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int KeySize = 32;  // AES-256
        private const int Pbkdf2Iterations = 600_000;

        private readonly ILogger<AesGcmEncryptionService> _logger;
        private readonly IEncryptionService _legacyFallback;

        public AesGcmEncryptionService(
            ILogger<AesGcmEncryptionService> logger,
            AesEncryptionService legacyFallback)
        {
            _logger = logger;
            _legacyFallback = legacyFallback;
        }

        public string Encrypt(string plainText, string key)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var keyBytes = DeriveKey(key, salt);
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = new byte[plainBytes.Length];
            var tag = new byte[TagSize];

            using var aesGcm = new AesGcm(keyBytes, TagSize);
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

            // Layout: [version(1)][salt(16)][nonce(12)][tag(16)][ciphertext]
            var output = new byte[1 + SaltSize + NonceSize + TagSize + cipherBytes.Length];
            output[0] = FormatVersion;
            Buffer.BlockCopy(salt, 0, output, 1, SaltSize);
            Buffer.BlockCopy(nonce, 0, output, 1 + SaltSize, NonceSize);
            Buffer.BlockCopy(tag, 0, output, 1 + SaltSize + NonceSize, TagSize);
            Buffer.BlockCopy(cipherBytes, 0, output, 1 + SaltSize + NonceSize + TagSize, cipherBytes.Length);

            return Convert.ToBase64String(output);
        }

        public string Decrypt(string cipherText, string key)
        {
            if (string.IsNullOrEmpty(cipherText)) return string.Empty;
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));

            byte[] data;
            try { data = Convert.FromBase64String(cipherText); }
            catch { return _legacyFallback.Decrypt(cipherText, key); }

            // Detect legacy CBC format (version byte will not be 0x02 or data too short)
            if (data.Length < 1 + SaltSize + NonceSize + TagSize + 1 || data[0] != FormatVersion)
            {
                _logger.LogDebug("Detected legacy AES-CBC ciphertext — falling back to CBC decryption.");
                return _legacyFallback.Decrypt(cipherText, key);
            }

            var salt = new byte[SaltSize];
            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];

            Buffer.BlockCopy(data, 1, salt, 0, SaltSize);
            Buffer.BlockCopy(data, 1 + SaltSize, nonce, 0, NonceSize);
            Buffer.BlockCopy(data, 1 + SaltSize + NonceSize, tag, 0, TagSize);

            var cipherLen = data.Length - 1 - SaltSize - NonceSize - TagSize;
            var cipherBytes = new byte[cipherLen];
            Buffer.BlockCopy(data, 1 + SaltSize + NonceSize + TagSize, cipherBytes, 0, cipherLen);

            var keyBytes = DeriveKey(key, salt);
            var plainBytes = new byte[cipherLen];

            using var aesGcm = new AesGcm(keyBytes, TagSize);
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }

        private static byte[] DeriveKey(string password, byte[] salt)
        {
            using var kdf = new Rfc2898DeriveBytes(
                password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
            return kdf.GetBytes(KeySize);
        }
    }
}
