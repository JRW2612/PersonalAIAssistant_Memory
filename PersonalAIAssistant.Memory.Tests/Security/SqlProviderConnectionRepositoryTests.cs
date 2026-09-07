using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Infrastructure.Context;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class SqlProviderConnectionRepositoryTests
    {
        private static ReadModelDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<ReadModelDbContext>()
                .UseInMemoryDatabase(databaseName: $"ProviderRepoTestDb_{Guid.NewGuid():N}")
                .Options;
            return new ReadModelDbContext(options);
        }

        [Fact]
        public async Task UpsertConnection_And_GetConnection_WorksCorrectly()
        {
            using var db = CreateDbContext();
            var repo = new SqlProviderConnectionRepository(db);

            var connection = new ProviderConnectionEntity
            {
                UserId = "user-bob",
                TenantId = "default",
                Provider = "gemini",
                EncryptedAccessToken = "enc-access-1",
                ConsentScopes = "ai.generate",
                Status = ProviderConnectionStatus.Active
            };

            await repo.UpsertConnectionAsync(connection);

            var retrieved = await repo.GetConnectionAsync("user-bob", "gemini");
            Assert.NotNull(retrieved);
            Assert.Equal("user-bob", retrieved.UserId);
            Assert.Equal("gemini", retrieved.Provider);
            Assert.Equal("enc-access-1", retrieved.EncryptedAccessToken);
            Assert.Equal(ProviderConnectionStatus.Active, retrieved.Status);

            // Update existing
            retrieved.EncryptedAccessToken = "enc-access-updated";
            await repo.UpsertConnectionAsync(retrieved);

            var updated = await repo.GetConnectionAsync("user-bob", "gemini");
            Assert.NotNull(updated);
            Assert.Equal("enc-access-updated", updated.EncryptedAccessToken);
        }

        [Fact]
        public async Task RevokeConnectionAsync_MarksRevoked_And_ClearsTokens()
        {
            using var db = CreateDbContext();
            var repo = new SqlProviderConnectionRepository(db);

            var connection = new ProviderConnectionEntity
            {
                UserId = "user-bob",
                TenantId = "default",
                Provider = "openai",
                EncryptedAccessToken = "enc-token-123",
                EncryptedRefreshToken = "enc-refresh-456",
                ConsentScopes = "ai.generate",
                Status = ProviderConnectionStatus.Active
            };

            await repo.UpsertConnectionAsync(connection);

            await repo.RevokeConnectionAsync("user-bob", "openai", "User revoked authorization");

            var revoked = await repo.GetConnectionAsync("user-bob", "openai");
            Assert.NotNull(revoked);
            Assert.Equal(ProviderConnectionStatus.Revoked, revoked.Status);
            Assert.Null(revoked.EncryptedAccessToken);
            Assert.Null(revoked.EncryptedRefreshToken);
            Assert.NotNull(revoked.RevokedAtUtc);
            Assert.Equal("User revoked authorization", revoked.RevocationReason);
        }

        [Fact]
        public async Task AddAuditLogAsync_Creates_HashChained_TamperEvident_Entries()
        {
            using var db = CreateDbContext();
            var repo = new SqlProviderConnectionRepository(db);

            var log1 = new ProviderAuditLogEntity
            {
                UserId = "user-bob",
                Provider = "gemini",
                EventType = "CONNECTED",
                Details = "Connected via OAuth"
            };

            await repo.AddAuditLogAsync(log1);

            var log2 = new ProviderAuditLogEntity
            {
                UserId = "user-bob",
                Provider = "gemini",
                EventType = "TOKEN_REFRESHED",
                Details = "Token refreshed automatically"
            };

            await repo.AddAuditLogAsync(log2);

            var logs = await repo.GetAuditLogsAsync("user-bob", "gemini");
            Assert.Equal(2, logs.Count);

            var recentLog = logs[0]; // TOKEN_REFRESHED
            var olderLog = logs[1];  // CONNECTED

            Assert.Equal("TOKEN_REFRESHED", recentLog.EventType);
            Assert.Equal("CONNECTED", olderLog.EventType);
            Assert.NotEmpty(olderLog.EntryHash);
            Assert.Equal(string.Empty, olderLog.PreviousHash);

            // Hash chain invariant: recent log's PreviousHash MUST match older log's EntryHash!
            Assert.Equal(olderLog.EntryHash, recentLog.PreviousHash);
            Assert.NotEmpty(recentLog.EntryHash);
        }
    }
}
