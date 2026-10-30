using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.AI
{
    /// <summary>
    /// Embedded in-process vector memory repository for local edge and offline execution.
    /// Provides persistent file-backed vector storage with cosine similarity search and metadata filtering,
    /// enabling the assistant to run completely offline without an external Qdrant container or cloud connection.
    /// </summary>
    public class EmbeddedEdgeVectorMemoryRepository : IVectorMemoryRepository
    {
        private readonly ConcurrentDictionary<Guid, EdgeVectorRecord> _vectors = new();
        private readonly string _storageFilePath;
        private readonly ILogger<EmbeddedEdgeVectorMemoryRepository> _logger;
        private readonly object _fileLock = new();
        private bool _isLoaded = false;

        public EmbeddedEdgeVectorMemoryRepository(
            IOptions<QdrantOptions> options,
            ILogger<EmbeddedEdgeVectorMemoryRepository> logger)
        {
            _logger = logger;
            var basePath = options.Value.LocalStoragePath ?? "./data/edge_vectors";
            try
            {
                if (!Directory.Exists(basePath))
                {
                    Directory.CreateDirectory(basePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EdgeVector] Unable to create directory {Path}. Using temp path.", basePath);
                basePath = Path.GetTempPath();
            }

            _storageFilePath = Path.Combine(basePath, $"{options.Value.CollectionName}_edge_store.json");
            LoadFromDisk();
        }

        public Task UpsertAsync(Guid memoryId, string embeddingId, IReadOnlyList<float> vector, string? userId, CancellationToken ct)
        {
            EnsureLoaded();
            var record = new EdgeVectorRecord(
                MemoryId: memoryId,
                EmbeddingId: embeddingId,
                Vector: vector.ToArray(),
                UserId: userId ?? string.Empty,
                TimestampUtc: DateTime.UtcNow
            );

            _vectors[memoryId] = record;
            SaveToDisk();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(IReadOnlyList<float> queryVector, int topK, string? userId, CancellationToken ct)
        {
            EnsureLoaded();
            var candidates = _vectors.Values.AsEnumerable();
            if (!string.IsNullOrEmpty(userId))
            {
                candidates = candidates.Where(c => string.Equals(c.UserId, userId, StringComparison.OrdinalIgnoreCase));
            }

            var results = candidates
                .Select(c => new VectorSearchResult(
                    MemoryId: c.MemoryId,
                    EmbeddingId: c.EmbeddingId,
                    Score: CosineSimilarity(queryVector, c.Vector)
                ))
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();

            return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
        }

        public Task DeleteAsync(Guid memoryId, CancellationToken ct)
        {
            EnsureLoaded();
            if (_vectors.TryRemove(memoryId, out _))
            {
                SaveToDisk();
            }
            return Task.CompletedTask;
        }

        private void EnsureLoaded()
        {
            if (!_isLoaded)
            {
                lock (_fileLock)
                {
                    if (!_isLoaded)
                    {
                        LoadFromDisk();
                    }
                }
            }
        }

        private void LoadFromDisk()
        {
            lock (_fileLock)
            {
                try
                {
                    if (File.Exists(_storageFilePath))
                    {
                        var json = File.ReadAllText(_storageFilePath);
                        var list = JsonSerializer.Deserialize<List<EdgeVectorRecord>>(json);
                        if (list != null)
                        {
                            _vectors.Clear();
                            foreach (var r in list)
                            {
                                _vectors[r.MemoryId] = r;
                            }
                            _logger.LogInformation("[EdgeVector] Loaded {Count} vector memories from disk storage {Path}", _vectors.Count, _storageFilePath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[EdgeVector] Could not load persisted vector store from {Path}. Starting with empty in-memory store.", _storageFilePath);
                }
                finally
                {
                    _isLoaded = true;
                }
            }
        }

        private void SaveToDisk()
        {
            lock (_fileLock)
            {
                try
                {
                    var list = _vectors.Values.ToList();
                    var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = false });
                    File.WriteAllText(_storageFilePath, json);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[EdgeVector] Could not persist vector memories to {Path}", _storageFilePath);
                }
            }
        }

        private static float CosineSimilarity(IReadOnlyList<float> vecA, float[] vecB)
        {
            if (vecA.Count != vecB.Length)
            {
                // If dimensionalities differ, evaluate projection on common prefix
                int minLen = Math.Min(vecA.Count, vecB.Length);
                if (minLen == 0) return 0f;
                float d = 0f, mA = 0f, mB = 0f;
                for (int i = 0; i < minLen; i++)
                {
                    d += vecA[i] * vecB[i];
                    mA += vecA[i] * vecA[i];
                    mB += vecB[i] * vecB[i];
                }
                if (mA <= 0f || mB <= 0f) return 0f;
                return d / (MathF.Sqrt(mA) * MathF.Sqrt(mB));
            }

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

        public sealed record EdgeVectorRecord(
            Guid MemoryId,
            string EmbeddingId,
            float[] Vector,
            string UserId,
            DateTime TimestampUtc);
    }
}
