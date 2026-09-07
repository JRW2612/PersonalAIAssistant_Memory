using PersonalAIAssistant.Memory.Core.Entities;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Security
{
    public interface IWorkloadIdentityRepository
    {
        Task<WorkloadIdentityEntity?> GetByClientIdAsync(string clientId, CancellationToken ct = default);
        Task<IReadOnlyList<WorkloadIdentityEntity>> GetByTenantIdAsync(string tenantId, CancellationToken ct = default);
        Task<WorkloadIdentityEntity?> FindMatchingOidcWorkloadAsync(string issuer, string audience, string subject, CancellationToken ct = default);
        Task UpsertAsync(WorkloadIdentityEntity entity, CancellationToken ct = default);
        Task RecordUsageAsync(string clientId, CancellationToken ct = default);
        Task RevokeAsync(string clientId, string reason, CancellationToken ct = default);
    }
}
