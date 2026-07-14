using PersonalAIAssistant.Memory.Core.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    public interface IVectorMemoryRepository
    {
        Task UpsertAsync(Guid memoryId, string embeddingId, IReadOnlyList<float> vector, CancellationToken ct);

        Task<IReadOnlyList<VectorSearchResult>> SearchAsync(IReadOnlyList<float> queryVector, int topK, CancellationToken ct);

        Task DeleteAsync(Guid memoryId, CancellationToken ct);
    }
}
