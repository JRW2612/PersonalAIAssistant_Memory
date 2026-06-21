using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.Sql
{
    public class SqlReadModelRepository : IReadModelRepository
    {
        public Task<IEnumerable<MemoryReadModel>> GetAllAsync(string queryText, int tokenBudget, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task UpsertAsync(MemoryReadModel model, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
