using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Infrastructure.Context;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class SqlWorkloadIdentityRepositoryTests
    {
        private static ReadModelDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ReadModelDbContext>()
                .UseInMemoryDatabase(databaseName: $"WorkloadRepoTestDb_{Guid.NewGuid():N}")
                .Options;
            return new ReadModelDbContext(options);
        }

        [Fact]
        public async Task UpsertAsync_And_GetByClientId_WorksCorrectly()
        {
            using var db = CreateDbContext();
            var repo = new SqlWorkloadIdentityRepository(db);

            var githubWorkload = new WorkloadIdentityEntity
            {
                ClientId = "github:myorg/myrepo",
                Name = "GitHub Actions CI",
                CloudPlatform = "GitHub",
                AuthType = WorkloadAuthType.OidcWorkloadIdentity,
                TenantId = "tenant-enterprise-1",
                AllowedScopes = "ai.memory.write",
                OidcIssuer = "https://token.actions.githubusercontent.com",
                OidcAudience = "https://memory-api.company.com",
                OidcSubjectFilter = "repo:myorg/myrepo:ref:refs/heads/main"
            };

            await repo.UpsertAsync(githubWorkload);

            var retrieved = await repo.GetByClientIdAsync("github:myorg/myrepo");
            Assert.NotNull(retrieved);
            Assert.Equal("GitHub Actions CI", retrieved.Name);
            Assert.Equal("tenant-enterprise-1", retrieved.TenantId);
            Assert.Equal(WorkloadStatus.Active, retrieved.Status);
        }

        [Fact]
        public async Task FindMatchingOidcWorkloadAsync_Matches_WildcardSubjectFilter()
        {
            using var db = CreateDbContext();
            var repo = new SqlWorkloadIdentityRepository(db);

            var awsWorkload = new WorkloadIdentityEntity
            {
                ClientId = "aws:bedrock-agent",
                Name = "AWS Bedrock Microservice",
                CloudPlatform = "AWS",
                AuthType = WorkloadAuthType.OidcWorkloadIdentity,
                TenantId = "tenant-aws-1",
                OidcIssuer = "https://oidc.eks.us-east-1.amazonaws.com/id/12345",
                OidcAudience = "https://memory-api.company.com",
                OidcSubjectFilter = "system:serviceaccount:ai-agents:*" // Wildcard matching any pod in namespace
            };

            await repo.UpsertAsync(awsWorkload);

            // Matching incoming subject
            var matched = await repo.FindMatchingOidcWorkloadAsync(
                issuer: "https://oidc.eks.us-east-1.amazonaws.com/id/12345",
                audience: "https://memory-api.company.com",
                subject: "system:serviceaccount:ai-agents:bedrock-summarizer"
            );

            Assert.NotNull(matched);
            Assert.Equal("aws:bedrock-agent", matched.ClientId);

            // Non-matching incoming subject from different namespace
            var notMatched = await repo.FindMatchingOidcWorkloadAsync(
                issuer: "https://oidc.eks.us-east-1.amazonaws.com/id/12345",
                audience: "https://memory-api.company.com",
                subject: "system:serviceaccount:other-ns:rogue-pod"
            );

            Assert.Null(notMatched);
        }

        [Fact]
        public async Task RevokeAsync_SetsRevokedStatus_And_Reason()
        {
            using var db = CreateDbContext();
            var repo = new SqlWorkloadIdentityRepository(db);

            var gcpWorkload = new WorkloadIdentityEntity
            {
                ClientId = "gcp:vertex-agent",
                Name = "GCP Vertex AI",
                CloudPlatform = "GCP",
                TenantId = "tenant-gcp-1",
                Status = WorkloadStatus.Active
            };

            await repo.UpsertAsync(gcpWorkload);
            await repo.RevokeAsync("gcp:vertex-agent", "Security audit rotation");

            var revoked = await repo.GetByClientIdAsync("gcp:vertex-agent");
            Assert.NotNull(revoked);
            Assert.Equal(WorkloadStatus.Revoked, revoked.Status);
            Assert.Equal("Security audit rotation", revoked.RevocationReason);
            Assert.NotNull(revoked.RevokedAtUtc);
        }

        [Fact]
        public async Task GetByTenantIdAsync_Enforces_StrictTenantIsolation()
        {
            using var db = CreateDbContext();
            var repo = new SqlWorkloadIdentityRepository(db);

            await repo.UpsertAsync(new WorkloadIdentityEntity
            {
                ClientId = "azure:openai-tenant-a",
                Name = "Tenant A Azure",
                TenantId = "tenant-a"
            });

            await repo.UpsertAsync(new WorkloadIdentityEntity
            {
                ClientId = "azure:openai-tenant-b",
                Name = "Tenant B Azure",
                TenantId = "tenant-b"
            });

            var tenantAWorkloads = await repo.GetByTenantIdAsync("tenant-a");
            var tenantBWorkloads = await repo.GetByTenantIdAsync("tenant-b");

            Assert.Single(tenantAWorkloads);
            Assert.Equal("azure:openai-tenant-a", tenantAWorkloads[0].ClientId);

            Assert.Single(tenantBWorkloads);
            Assert.Equal("azure:openai-tenant-b", tenantBWorkloads[0].ClientId);
        }
    }
}
