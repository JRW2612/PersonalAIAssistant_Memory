using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Infrastructure.Context;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    public class SqlWorkloadIdentityRepository : IWorkloadIdentityRepository
    {
        private readonly ReadModelDbContext _db;

        public SqlWorkloadIdentityRepository(ReadModelDbContext db)
        {
            _db = db;
        }

        public async Task<WorkloadIdentityEntity?> GetByClientIdAsync(string clientId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(clientId)) return null;

            var normalized = clientId.Trim().ToLowerInvariant();
            return await _db.WorkloadIdentities
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.ClientId.ToLower() == normalized, ct);
        }

        public async Task<IReadOnlyList<WorkloadIdentityEntity>> GetByTenantIdAsync(string tenantId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return Array.Empty<WorkloadIdentityEntity>();

            return await _db.WorkloadIdentities
                .AsNoTracking()
                .Where(w => w.TenantId == tenantId)
                .OrderBy(w => w.CloudPlatform)
                .ThenBy(w => w.Name)
                .ToListAsync(ct);
        }

        public async Task<WorkloadIdentityEntity?> FindMatchingOidcWorkloadAsync(
            string issuer, string audience, string subject, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
                return null;

            var normalizedIssuer = NormalizeUrl(issuer);

            // Fetch candidate active OIDC workloads
            var candidates = await _db.WorkloadIdentities
                .Where(w => w.Status == WorkloadStatus.Active && w.AuthType == WorkloadAuthType.OidcWorkloadIdentity)
                .ToListAsync(ct);

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.OidcIssuer)) continue;

                if (!NormalizeUrl(candidate.OidcIssuer).Equals(normalizedIssuer, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Validate audience if specified on the workload registration
                if (!string.IsNullOrWhiteSpace(candidate.OidcAudience) &&
                    !string.IsNullOrWhiteSpace(audience) &&
                    !candidate.OidcAudience.Equals(audience, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Match subject pattern (supports wildcards e.g. "repo:my-org/*")
                if (IsSubjectMatch(candidate.OidcSubjectFilter, subject))
                {
                    return candidate;
                }
            }

            return null;
        }

        public async Task UpsertAsync(WorkloadIdentityEntity entity, CancellationToken ct = default)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            entity.ClientId = entity.ClientId.Trim().ToLowerInvariant();
            var existing = await _db.WorkloadIdentities
                .FirstOrDefaultAsync(w => w.ClientId.ToLower() == entity.ClientId, ct);

            if (existing != null)
            {
                existing.Name = entity.Name;
                existing.CloudPlatform = entity.CloudPlatform;
                existing.AuthType = entity.AuthType;
                existing.TenantId = entity.TenantId;
                existing.AllowedScopes = entity.AllowedScopes;
                existing.HashedSecret = entity.HashedSecret ?? existing.HashedSecret;
                existing.OidcIssuer = entity.OidcIssuer;
                existing.OidcAudience = entity.OidcAudience;
                existing.OidcSubjectFilter = entity.OidcSubjectFilter;
                existing.EncryptedWebhookSecret = entity.EncryptedWebhookSecret ?? existing.EncryptedWebhookSecret;
                existing.Status = entity.Status;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                entity.CreatedAtUtc = DateTime.UtcNow;
                entity.UpdatedAtUtc = DateTime.UtcNow;
                await _db.WorkloadIdentities.AddAsync(entity, ct);
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task RecordUsageAsync(string clientId, CancellationToken ct = default)
        {
            var normalized = clientId.Trim().ToLowerInvariant();
            var existing = await _db.WorkloadIdentities
                .FirstOrDefaultAsync(w => w.ClientId.ToLower() == normalized, ct);

            if (existing != null)
            {
                existing.LastUsedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        public async Task RevokeAsync(string clientId, string reason, CancellationToken ct = default)
        {
            var normalized = clientId.Trim().ToLowerInvariant();
            var existing = await _db.WorkloadIdentities
                .FirstOrDefaultAsync(w => w.ClientId.ToLower() == normalized, ct);

            if (existing != null)
            {
                existing.Status = WorkloadStatus.Revoked;
                existing.RevokedAtUtc = DateTime.UtcNow;
                existing.RevocationReason = reason;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        private static bool IsSubjectMatch(string? pattern, string subject)
        {
            if (string.IsNullOrWhiteSpace(pattern) || pattern == "*")
                return true;

            pattern = pattern.Trim();
            subject = subject.Trim();

            if (pattern.EndsWith('*'))
            {
                var prefix = pattern.TrimEnd('*');
                return subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }

            return pattern.Equals(subject, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeUrl(string url) => url.Trim().TrimEnd('/');
    }
}
