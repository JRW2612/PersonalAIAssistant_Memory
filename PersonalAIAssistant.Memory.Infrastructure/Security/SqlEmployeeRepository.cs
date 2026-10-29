using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.Context;

namespace PersonalAIAssistant.Memory.Infrastructure.Security;

public class SqlEmployeeRepository : IEmployeeRepository
{
    private readonly ReadModelDbContext _db;

    public SqlEmployeeRepository(ReadModelDbContext db)
    {
        _db = db;
    }

    public async Task<EmployeeRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.EmployeeRecords.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<EmployeeRecord?> GetByIdAndTenantAsync(Guid id, string tenantId, CancellationToken ct = default)
    {
        return await _db.EmployeeRecords.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<EmployeeRecord>> GetByTenantAsync(string tenantId, CancellationToken ct = default)
    {
        return await _db.EmployeeRecords.Where(e => e.TenantId == tenantId).ToListAsync(ct);
    }

    public async Task AddAsync(EmployeeRecord record, CancellationToken ct = default)
    {
        _db.EmployeeRecords.Add(record);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(EmployeeRecord record, CancellationToken ct = default)
    {
        _db.EmployeeRecords.Update(record);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await _db.EmployeeRecords.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (existing != null)
        {
            _db.EmployeeRecords.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
    }
}
