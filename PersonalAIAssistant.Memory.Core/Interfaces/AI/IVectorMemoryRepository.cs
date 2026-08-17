using PersonalAIAssistant.Memory.Core.DTOs;

namespace PersonalAIAssistant.Memory.Core.Interfaces.AI
{
    public interface IVectorMemoryRepository
    {
        Task UpsertAsync(Guid memoryId, string embeddingId, IReadOnlyList<float> vector, string? userId, CancellationToken ct);
        Task<IReadOnlyList<VectorSearchResult>> SearchAsync(IReadOnlyList<float> queryVector, int topK, string? userId, CancellationToken ct);
        Task DeleteAsync(Guid memoryId, CancellationToken ct);
    }
}
