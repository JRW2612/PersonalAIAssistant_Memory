using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using System.Security.Cryptography;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class AesGcmEncryptionServiceTests
    {
        private static AesGcmEncryptionService CreateService()
        {
            var legacy = new AesEncryptionService();
            return new AesGcmEncryptionService(NullLogger<AesGcmEncryptionService>.Instance, legacy);
        }

        [Fact]
        public void Encrypt_Decrypt_Roundtrip_RestoresOriginalText()
        {
            var svc = CreateService();
            var key = "TestKey_UserAbc";
            var original = "Corporate memory: Q3 roadmap discussion.";

            var encrypted = svc.Encrypt(original, key);
            var decrypted = svc.Decrypt(encrypted, key);

            decrypted.Should().Be(original);
        }

        [Fact]
        public void Encrypt_ProducesDifferentCiphertextEachCall_DueToRandomSalt()
        {
            var svc = CreateService();
            var key = "SameKey";
            var text = "Same plaintext";

            var cipher1 = svc.Encrypt(text, key);
            var cipher2 = svc.Encrypt(text, key);

            cipher1.Should().NotBe(cipher2);
        }

        [Fact]
        public void Decrypt_TamperedCiphertext_ThrowsAuthenticationTagMismatch()
        {
            var svc = CreateService();
            var key = "TestKey";
            var encrypted = svc.Encrypt("Sensitive data", key);

            // Tamper with the base64: flip a character in the ciphertext portion
            var bytes = Convert.FromBase64String(encrypted);
            bytes[bytes.Length - 1] ^= 0xFF;
            var tampered = Convert.ToBase64String(bytes);

            Action act = () => svc.Decrypt(tampered, key);
            act.Should().Throw<AuthenticationTagMismatchException>();
        }

        [Fact]
        public void Decrypt_EmptyString_ReturnsEmpty()
        {
            var svc = CreateService();
            svc.Decrypt(string.Empty, "key").Should().BeEmpty();
        }

        [Fact]
        public void Decrypt_LegacyCbcCiphertext_FallsBackGracefully()
        {
            // Encrypt using legacy AES-CBC service
            var legacy = new AesEncryptionService();
            var key = "LegacyKey";
            var original = "Old encrypted memory";
            var legacyCipher = legacy.Encrypt(original, key);

            // AesGcmEncryptionService should transparently decrypt it
            var svc = CreateService();
            var decrypted = svc.Decrypt(legacyCipher, key);
            decrypted.Should().Be(original);
        }
    }
}
