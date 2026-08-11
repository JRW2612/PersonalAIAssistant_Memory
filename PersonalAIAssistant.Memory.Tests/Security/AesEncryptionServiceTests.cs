using FluentAssertions;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using System;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class AesEncryptionServiceTests
    {
        private readonly AesEncryptionService _service;

        public AesEncryptionServiceTests()
        {
            _service = new AesEncryptionService();
        }

        [Fact]
        public void Encrypt_And_Decrypt_Should_Restore_Original_Text()
        {
            // Arrange
            var originalText = "Confidential user memory payload requiring AES-256 protection.";
            var userSecretKey = "SystemKey_user-789-secret-key-derivation";

            // Act
            var cipherText = _service.Encrypt(originalText, userSecretKey);
            var decryptedText = _service.Decrypt(cipherText, userSecretKey);

            // Assert
            cipherText.Should().NotBeNullOrEmpty();
            cipherText.Should().NotBe(originalText);
            decryptedText.Should().Be(originalText);
        }

        [Fact]
        public void Decrypt_With_Wrong_Key_Should_Throw_Or_Fail()
        {
            // Arrange
            var originalText = "Secret data";
            var correctKey = "CorrectKey1234567890123456789012";
            var wrongKey = "WrongKey123456789012345678901234";

            var cipherText = _service.Encrypt(originalText, correctKey);

            // Act & Assert
            Action act = () => _service.Decrypt(cipherText, wrongKey);
            act.Should().Throw<Exception>();
        }

        [Fact]
        public void Encrypt_Null_Or_Empty_Should_Return_Empty_String()
        {
            var cipherText = _service.Encrypt("", "Key123");
            cipherText.Should().BeEmpty();
        }
    }
}
