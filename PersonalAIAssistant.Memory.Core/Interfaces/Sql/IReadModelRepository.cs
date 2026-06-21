using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.Sql
{
    public interface IReadModelRepository
    {
        Task UpsertAsync(MemoryReadModel model, CancellationToken cancellationToken);
        Task<IEnumerable<MemoryReadModel>> GetAllAsync(string queryText, int tokenBudget, CancellationToken cancellationToken);
    }
}
