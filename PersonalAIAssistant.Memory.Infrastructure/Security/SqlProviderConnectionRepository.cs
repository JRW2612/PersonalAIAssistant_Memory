using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Infrastructure.Context;
using PersonalAIAssistant.Memory.Infrastructure.Extensions;

namespace PersonalAIAssistant.Memory.Infrastructure.Security;

/// <summary>EF-backed, tenant-aware storage for encrypted user provider connections and hash-chained audit entries.</summary>
public sealed class SqlProviderConnectionRepository : IProviderConnectionRepository
{
    private readonly ReadModelDbContext _db;
    private readonly IUserContext? _userContext;

    public SqlProviderConnectionRepository(ReadModelDbContext db, IUserContext? userContext = null)
    {
        _db = db;
        _userContext = userContext;
    }

    public Task<ProviderConnectionEntity?> GetConnectionAsync(string userId, string provider, CancellationToken ct = default) =>
        _db.ProviderConnections
            .TagWithUserAction(_userContext, "ProviderConnection.GetConnection")
            .SingleOrDefaultAsync(c => c.UserId == userId && c.Provider == Normalize(provider), ct);

    public async Task<IReadOnlyList<ProviderConnectionEntity>> GetUserConnectionsAsync(string userId, CancellationToken ct = default) =>
        await _db.ProviderConnections
            .TagWithUserAction(_userContext, "ProviderConnection.GetUserConnections")
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Provider)
            .ToListAsync(ct);

    public async Task UpsertConnectionAsync(ProviderConnectionEntity connection, CancellationToken ct = default)
    {
        connection.Provider = Normalize(connection.Provider);
        var existing = await GetConnectionAsync(connection.UserId, connection.Provider, ct);
        if (existing is null)
        {
            connection.CreatedAtUtc = DateTime.UtcNow;
            connection.UpdatedAtUtc = connection.CreatedAtUtc;
            _db.ProviderConnections.Add(connection);
        }
        else
        {
            existing.TenantId = connection.TenantId;
            existing.EncryptedAccessToken = connection.EncryptedAccessToken;
            existing.EncryptedRefreshToken = connection.EncryptedRefreshToken;
            existing.TokenExpiresAtUtc = connection.TokenExpiresAtUtc;
            existing.ConsentScopes = connection.ConsentScopes;
            existing.Status = connection.Status;
            existing.CredentialKind = connection.CredentialKind;
            existing.AccountEmail = connection.AccountEmail;
            existing.AccountName = connection.AccountName;
            existing.RevokedAtUtc = connection.RevokedAtUtc;
            existing.RevocationReason = connection.RevocationReason;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeConnectionAsync(string userId, string provider, string reason, CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(userId, provider, ct);
        if (connection is null) return;
        connection.Status = ProviderConnectionStatus.Revoked;
        connection.EncryptedAccessToken = null;
        connection.EncryptedRefreshToken = null;
        connection.TokenExpiresAtUtc = null;
        connection.RevokedAtUtc = DateTime.UtcNow;
        connection.RevocationReason = reason[..Math.Min(reason.Length, 500)];
        connection.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddAuditLogAsync(ProviderAuditLogEntity auditLog, CancellationToken ct = default)
    {
        var priorHash = await _db.ProviderAuditLogs
            .TagWithUserAction(_userContext, "ProviderAuditLog.GetPriorHash")
            .Where(x => x.UserId == auditLog.UserId && x.Provider == Normalize(auditLog.Provider))
            .OrderByDescending(x => x.TimestampUtc)
            .Select(x => x.EntryHash)
            .FirstOrDefaultAsync(ct) ?? string.Empty;
        auditLog.Provider = Normalize(auditLog.Provider);
        auditLog.TimestampUtc = DateTime.UtcNow;
        auditLog.PreviousHash = priorHash;
        auditLog.EntryHash = Hash($"{auditLog.UserId}|{auditLog.TenantId}|{auditLog.Provider}|{auditLog.EventType}|{auditLog.Details}|{auditLog.TimestampUtc:O}|{priorHash}");
        _db.ProviderAuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ProviderAuditLogEntity>> GetAuditLogsAsync(string userId, string? provider = null, int limit = 50, CancellationToken ct = default)
    {
        var query = _db.ProviderAuditLogs
            .TagWithUserAction(_userContext, "ProviderAuditLog.GetAuditLogs")
            .Where(x => x.UserId == userId);
        if (!string.IsNullOrWhiteSpace(provider)) query = query.Where(x => x.Provider == Normalize(provider));
        return await query.OrderByDescending(x => x.TimestampUtc).Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct);
    }

    private static string Normalize(string provider) => provider.Trim().ToLowerInvariant();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
