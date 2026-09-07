using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using System.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class OAuthStateManagerTests
    {
        private static OAuthStateManager CreateManager(string key = "TestHmacSecretKeyMustBe32BytesLongSecret!")
        {
            var opts = Options.Create(new EncryptionOptions { SystemKey = key });
            return new OAuthStateManager(opts);
        }

        [Fact]
        public void Create_And_Consume_ValidState_Succeeds()
        {
            var manager = CreateManager();
            var scopes = new[] { "ai.generate", "ai.memory.read" };

            var state = manager.Create("user-alice", "tenant-1", "gemini", scopes);

            Assert.NotNull(state);
            Assert.Contains(".", state);

            var consumed = manager.Consume(state);

            Assert.Equal("user-alice", consumed.UserId);
            Assert.Equal("tenant-1", consumed.TenantId);
            Assert.Equal("gemini", consumed.Provider);
            Assert.Equal(2, consumed.Scopes.Count);
            Assert.Contains("ai.generate", consumed.Scopes);
            Assert.Contains("ai.memory.read", consumed.Scopes);
            Assert.True(consumed.ExpiresAtUtc > DateTime.UtcNow);
        }

        [Fact]
        public void Consume_TamperedState_ThrowsSecurityException()
        {
            var manager = CreateManager();
            var state = manager.Create("user-alice", "tenant-1", "gemini", new[] { "ai.generate" });

            var parts = state.Split('.');
            var tamperedPayload = parts[0] + "xyz";
            var tamperedState = $"{tamperedPayload}.{parts[1]}";

            Assert.Throws<SecurityException>(() => manager.Consume(tamperedState));
        }

        [Fact]
        public void Consume_TamperedSignature_ThrowsSecurityException()
        {
            var manager = CreateManager();
            var state = manager.Create("user-alice", "tenant-1", "gemini", new[] { "ai.generate" });

            var parts = state.Split('.');
            var tamperedState = $"{parts[0]}.invalid_signature";

            Assert.Throws<SecurityException>(() => manager.Consume(tamperedState));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public void Consume_EmptyOrNullState_ThrowsArgumentException(string? invalidState)
        {
            var manager = CreateManager();
            Assert.Throws<ArgumentException>(() => manager.Consume(invalidState!));
        }

        [Theory]
        [InlineData("singleparttoken")]
        [InlineData("part1.part2.part3")]
        public void Consume_MalformedState_ThrowsInvalidOperationException(string malformedState)
        {
            var manager = CreateManager();
            Assert.Throws<InvalidOperationException>(() => manager.Consume(malformedState));
        }
    }
}
