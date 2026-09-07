using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class WorkloadAuthenticationServiceTests
    {
        private readonly Mock<IWorkloadIdentityRepository> _repoMock;
        private readonly Mock<ISecurityAuditLogger> _auditLoggerMock;
        private readonly AesGcmEncryptionService _encryptionService;
        private readonly IOptions<EncryptionOptions> _encryptionOpts;

        public WorkloadAuthenticationServiceTests()
        {
            _repoMock = new Mock<IWorkloadIdentityRepository>();
            _auditLoggerMock = new Mock<ISecurityAuditLogger>();
            var legacy = new AesEncryptionService();
            _encryptionService = new AesGcmEncryptionService(NullLogger<AesGcmEncryptionService>.Instance, legacy);
            _encryptionOpts = Options.Create(new EncryptionOptions
            {
                SystemKey = "TestSystemEncryptionKey32BytesLongSecretString!"
            });
        }

        private WorkloadAuthenticationService CreateService()
        {
            return new WorkloadAuthenticationService(
                _repoMock.Object,
                _encryptionService,
                _auditLoggerMock.Object,
                _encryptionOpts,
                NullLogger<WorkloadAuthenticationService>.Instance
            );
        }

        [Fact]
        public async Task AuthenticateClientCredentialsAsync_Succeeds_WhenSecretMatches()
        {
            var service = CreateService();
            var rawSecret = "super-secret-m2m-password-123";
            var hashedSecret = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawSecret))).ToLowerInvariant();

            var workload = new WorkloadIdentityEntity
            {
                ClientId = "azure:microservice-1",
                Name = "Azure Ingestion Worker",
                Status = WorkloadStatus.Active,
                HashedSecret = hashedSecret
            };

            _repoMock.Setup(r => r.GetByClientIdAsync("azure:microservice-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(workload);

            var authenticated = await service.AuthenticateClientCredentialsAsync("azure:microservice-1", rawSecret);

            Assert.NotNull(authenticated);
            Assert.Equal("azure:microservice-1", authenticated.ClientId);
            _repoMock.Verify(r => r.RecordUsageAsync("azure:microservice-1", It.IsAny<CancellationToken>()), Times.Once);
            _auditLoggerMock.Verify(a => a.LogAccessGranted("azure:microservice-1", "M2M_AUTH", "azure:microservice-1"), Times.Once);
        }

        [Fact]
        public async Task AuthenticateClientCredentialsAsync_Rejects_WhenSecretWrong()
        {
            var service = CreateService();
            var hashedSecret = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("correct-secret"))).ToLowerInvariant();

            var workload = new WorkloadIdentityEntity
            {
                ClientId = "aws:lambda-1",
                Status = WorkloadStatus.Active,
                HashedSecret = hashedSecret
            };

            _repoMock.Setup(r => r.GetByClientIdAsync("aws:lambda-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(workload);

            var authenticated = await service.AuthenticateClientCredentialsAsync("aws:lambda-1", "wrong-secret");

            Assert.Null(authenticated);
            _auditLoggerMock.Verify(a => a.LogAccessDenied("aws:lambda-1", "M2M_AUTH", "aws:lambda-1", "Invalid client secret"), Times.Once);
        }

        [Fact]
        public async Task AuthenticateOidcTokenAsync_Resolves_MatchingWorkload()
        {
            var service = CreateService();
            var payloadJson = """
            {
                "iss": "https://token.actions.githubusercontent.com",
                "aud": "https://memory-api.company.com",
                "sub": "repo:my-org/my-repo:ref:refs/heads/main"
            }
            """;
            var headerB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var mockJwt = $"{headerB64}.{payloadB64}.signature";

            var matchedWorkload = new WorkloadIdentityEntity
            {
                ClientId = "github:my-org/my-repo",
                Name = "GitHub Actions",
                CloudPlatform = "GitHub",
                Status = WorkloadStatus.Active
            };

            _repoMock.Setup(r => r.FindMatchingOidcWorkloadAsync(
                "https://token.actions.githubusercontent.com",
                "https://memory-api.company.com",
                "repo:my-org/my-repo:ref:refs/heads/main",
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(matchedWorkload);

            var authenticated = await service.AuthenticateOidcTokenAsync(mockJwt);

            Assert.NotNull(authenticated);
            Assert.Equal("github:my-org/my-repo", authenticated.ClientId);
            _repoMock.Verify(r => r.RecordUsageAsync("github:my-org/my-repo", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task VerifyWebhookSignatureAsync_Verifies_GitHub_Hmac_Signature()
        {
            var service = CreateService();
            var webhookSecret = "gh-webhook-secret-xyz-456";
            var encryptedSecret = _encryptionService.Encrypt(webhookSecret, "TestSystemEncryptionKey32BytesLongSecretString!");

            var payload = "{\"action\":\"completed\",\"workflow\":\"ci-memory-sync\"}";

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
            var hashHex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            var signatureHeader = $"sha256={hashHex}";

            var workload = new WorkloadIdentityEntity
            {
                ClientId = "github:webhook-ingester",
                Status = WorkloadStatus.Active,
                EncryptedWebhookSecret = encryptedSecret
            };

            _repoMock.Setup(r => r.GetByClientIdAsync("github:webhook-ingester", It.IsAny<CancellationToken>()))
                .ReturnsAsync(workload);

            var isValid = await service.VerifyWebhookSignatureAsync("github:webhook-ingester", payload, signatureHeader);
            Assert.True(isValid);

            var isTamperedValid = await service.VerifyWebhookSignatureAsync("github:webhook-ingester", payload + "tampered", signatureHeader);
            Assert.False(isTamperedValid);
        }
    }
}
