using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class ProviderTokenServiceTests
    {
        private readonly Mock<IProviderConnectionRepository> _repoMock;
        private readonly Mock<ISecurityAuditLogger> _auditLoggerMock;
        private readonly AesGcmEncryptionService _encryptionService;
        private readonly IOptions<EncryptionOptions> _encryptionOpts;

        public ProviderTokenServiceTests()
        {
            _repoMock = new Mock<IProviderConnectionRepository>();
            _auditLoggerMock = new Mock<ISecurityAuditLogger>();
            var legacy = new AesEncryptionService();
            _encryptionService = new AesGcmEncryptionService(NullLogger<AesGcmEncryptionService>.Instance, legacy);
            _encryptionOpts = Options.Create(new EncryptionOptions
            {
                SystemKey = "TestSystemEncryptionKey32BytesLongSecretString!"
            });
        }

        private ProviderTokenService CreateService(IEnumerable<IOAuthProviderHandler>? handlers = null)
        {
            return new ProviderTokenService(
                _repoMock.Object,
                _encryptionService,
                _auditLoggerMock.Object,
                handlers ?? Enumerable.Empty<IOAuthProviderHandler>(),
                _encryptionOpts,
                NullLogger<ProviderTokenService>.Instance
            );
        }

        [Fact]
        public void EncryptToken_And_DecryptToken_RoundTrips_Successfully()
        {
            var service = CreateService();
            var rawToken = "ya29.a0AfH6SMD-test-google-oauth-bearer-token";

            var encrypted = service.EncryptToken(rawToken);

            Assert.NotEqual(rawToken, encrypted);
            Assert.False(string.IsNullOrWhiteSpace(encrypted));

            var decrypted = service.DecryptToken(encrypted);
            Assert.Equal(rawToken, decrypted);
        }

        [Fact]
        public async Task HasScopeConsentAsync_ReturnsTrue_When_ScopeIsGranted()
        {
            var service = CreateService();
            var connection = new ProviderConnectionEntity
            {
                UserId = "user-alice",
                Provider = "gemini",
                Status = ProviderConnectionStatus.Active,
                ConsentScopes = "ai.generate ai.memory.read"
            };

            _repoMock.Setup(r => r.GetConnectionAsync("user-alice", "gemini", It.IsAny<CancellationToken>()))
                .ReturnsAsync(connection);

            var hasGenerate = await service.HasScopeConsentAsync("user-alice", "gemini", "ai.generate");
            var hasRead = await service.HasScopeConsentAsync("user-alice", "gemini", "ai.memory.read");
            var hasWrite = await service.HasScopeConsentAsync("user-alice", "gemini", "ai.memory.write");

            Assert.True(hasGenerate);
            Assert.True(hasRead);
            Assert.False(hasWrite);
        }

        [Fact]
        public async Task GetValidAccessTokenAsync_ReturnsPlaintextToken_When_ActiveAndConsented()
        {
            var service = CreateService();
            var rawToken = "gemini-live-valid-token-12345";
            var encrypted = service.EncryptToken(rawToken);

            var connection = new ProviderConnectionEntity
            {
                UserId = "user-alice",
                Provider = "gemini",
                Status = ProviderConnectionStatus.Active,
                EncryptedAccessToken = encrypted,
                ConsentScopes = "ai.generate ai.memory.read",
                TokenExpiresAtUtc = DateTime.UtcNow.AddHours(2)
            };

            _repoMock.Setup(r => r.GetConnectionAsync("user-alice", "gemini", It.IsAny<CancellationToken>()))
                .ReturnsAsync(connection);

            var result = await service.GetValidAccessTokenAsync("user-alice", "gemini", "ai.generate");

            Assert.Equal(rawToken, result);
            _auditLoggerMock.Verify(a => a.LogProviderAccessAttempt("user-alice", "gemini", "GetValidAccessToken", "GRANTED", null), Times.Once);
        }

        [Fact]
        public async Task GetValidAccessTokenAsync_ReturnsNull_When_ScopeMissing()
        {
            var service = CreateService();
            var connection = new ProviderConnectionEntity
            {
                UserId = "user-alice",
                Provider = "gemini",
                Status = ProviderConnectionStatus.Active,
                EncryptedAccessToken = service.EncryptToken("raw-token"),
                ConsentScopes = "ai.memory.read", // Missing ai.generate
                TokenExpiresAtUtc = DateTime.UtcNow.AddHours(2)
            };

            _repoMock.Setup(r => r.GetConnectionAsync("user-alice", "gemini", It.IsAny<CancellationToken>()))
                .ReturnsAsync(connection);

            var result = await service.GetValidAccessTokenAsync("user-alice", "gemini", "ai.generate");

            Assert.Null(result);
            _auditLoggerMock.Verify(a => a.LogProviderAccessAttempt("user-alice", "gemini", "GetValidAccessToken", "DENIED", It.Is<string>(s => s.Contains("Missing required scope"))), Times.Once);
        }

        [Fact]
        public async Task GetValidAccessTokenAsync_ReturnsNull_When_ConnectionRevoked()
        {
            var service = CreateService();
            var connection = new ProviderConnectionEntity
            {
                UserId = "user-alice",
                Provider = "gemini",
                Status = ProviderConnectionStatus.Revoked,
                EncryptedAccessToken = service.EncryptToken("raw-token"),
                ConsentScopes = "ai.generate"
            };

            _repoMock.Setup(r => r.GetConnectionAsync("user-alice", "gemini", It.IsAny<CancellationToken>()))
                .ReturnsAsync(connection);

            var result = await service.GetValidAccessTokenAsync("user-alice", "gemini", "ai.generate");

            Assert.Null(result);
            _auditLoggerMock.Verify(a => a.LogProviderAccessAttempt("user-alice", "gemini", "GetValidAccessToken", "DENIED", "Connection is revoked"), Times.Once);
        }

        [Fact]
        public async Task GetValidAccessTokenAsync_RefreshesToken_WhenExpiredAndRefreshTokenPresent()
        {
            var handlerMock = new Mock<IOAuthProviderHandler>();
            handlerMock.Setup(h => h.ProviderName).Returns("gemini");
            handlerMock.Setup(h => h.RefreshTokenAsync("refresh-token-123", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OAuthTokenResult("new-access-token-999", "refresh-token-123", DateTime.UtcNow.AddHours(1)));

            var service = CreateService(new[] { handlerMock.Object });

            var connection = new ProviderConnectionEntity
            {
                UserId = "user-alice",
                Provider = "gemini",
                Status = ProviderConnectionStatus.Active,
                EncryptedAccessToken = service.EncryptToken("old-expired-token"),
                EncryptedRefreshToken = service.EncryptToken("refresh-token-123"),
                ConsentScopes = "ai.generate",
                TokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5) // Expired 5 mins ago
            };

            _repoMock.Setup(r => r.GetConnectionAsync("user-alice", "gemini", It.IsAny<CancellationToken>()))
                .ReturnsAsync(connection);

            var result = await service.GetValidAccessTokenAsync("user-alice", "gemini", "ai.generate");

            Assert.Equal("new-access-token-999", result);
            _repoMock.Verify(r => r.UpsertConnectionAsync(It.IsAny<ProviderConnectionEntity>(), It.IsAny<CancellationToken>()), Times.Once);
            _auditLoggerMock.Verify(a => a.LogProviderTokenRefreshed("user-alice", "gemini"), Times.Once);
        }
    }
}
