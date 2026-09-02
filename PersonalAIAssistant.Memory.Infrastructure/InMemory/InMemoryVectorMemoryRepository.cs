using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using System.Collections.Concurrent;

namespace PersonalAIAssistant.Memory.Infrastructure.InMemory
{
    /// <summary>
    /// In-memory vector memory repository for local development, testing, and offline modes.
    /// Performs exact cosine similarity search without needing an external vector database like Qdrant.
    /// </summary>
    public class InMemoryVectorMemoryRepository : IVectorMemoryRepository
    {
        private readonly ConcurrentDictionary<Guid, (string EmbeddingId, float[] Vector, string? UserId)> _store = new();

        public Task UpsertAsync(Guid memoryId, string embeddingId, IReadOnlyList<float> vector, string? userId, CancellationToken ct)
        {
            _store[memoryId] = (embeddingId, vector.ToArray(), userId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(IReadOnlyList<float> queryVector, int topK, string? userId, CancellationToken ct)
        {
            var query = _store.AsEnumerable();
            if (!string.IsNullOrEmpty(userId))
            {
                query = query.Where(kvp => kvp.Value.UserId == userId);
            }

            var results = query.Select(kvp => new VectorSearchResult(
                MemoryId: kvp.Key,
                EmbeddingId: kvp.Value.EmbeddingId,
                Score: CosineSimilarity(queryVector, kvp.Value.Vector)
            ))
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
        }

        public Task DeleteAsync(Guid memoryId, CancellationToken ct)
        {
            _store.TryRemove(memoryId, out _);
            return Task.CompletedTask;
        }

        private static float CosineSimilarity(IReadOnlyList<float> vecA, float[] vecB)
        {
            if (vecA.Count != vecB.Length) return 0f;
            float dot = 0f, magA = 0f, magB = 0f;
            for (int i = 0; i < vecA.Count; i++)
            {
                dot += vecA[i] * vecB[i];
                magA += vecA[i] * vecA[i];
                magB += vecB[i] * vecB[i];
            }
            if (magA <= 0f || magB <= 0f) return 0f;
            return dot / (MathF.Sqrt(magA) * MathF.Sqrt(magB));
        }
    }
}
