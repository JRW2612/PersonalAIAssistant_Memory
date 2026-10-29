using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Security;

public interface IEmployeeRepository
{
    Task<EmployeeRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<EmployeeRecord?> GetByIdAndTenantAsync(Guid id, string tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeRecord>> GetByTenantAsync(string tenantId, CancellationToken ct = default);
    Task AddAsync(EmployeeRecord record, CancellationToken ct = default);
    Task UpdateAsync(EmployeeRecord record, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
