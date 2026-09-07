using PersonalAIAssistant.Memory.Core.Entities;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Security;

public interface IProviderConnectionRepository
{
    Task<ProviderConnectionEntity?> GetConnectionAsync(string userId, string provider, CancellationToken ct = default);
    Task<IReadOnlyList<ProviderConnectionEntity>> GetUserConnectionsAsync(string userId, CancellationToken ct = default);
    Task UpsertConnectionAsync(ProviderConnectionEntity connection, CancellationToken ct = default);
    Task RevokeConnectionAsync(string userId, string provider, string reason, CancellationToken ct = default);
    Task AddAuditLogAsync(ProviderAuditLogEntity auditLog, CancellationToken ct = default);
    Task<IReadOnlyList<ProviderAuditLogEntity>> GetAuditLogsAsync(string userId, string? provider = null, int limit = 50, CancellationToken ct = default);
}
