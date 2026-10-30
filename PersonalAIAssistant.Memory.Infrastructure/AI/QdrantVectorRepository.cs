using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace PersonalAIAssistant.Memory.Infrastructure.AI
{
    /// <summary>
    /// Vector repository that dynamically supports both Qdrant:Cloud (managed cluster with API Key)
    /// and Qdrant:LocalEdge (local developer container or embedded in-process file-backed store for offline edge mode).
    /// </summary>
    public class QdrantVectorRepository : IVectorMemoryRepository
    {
        private readonly QdrantClient _client;
        private readonly QdrantOptions _options;
        private readonly EmbeddedEdgeVectorMemoryRepository _edgeFallback;
        private readonly ILogger<QdrantVectorRepository> _logger;
        private readonly string _collectionName;
        private bool _localContainerFailed = false;

        public QdrantVectorRepository(
            QdrantClient client,
            IOptions<QdrantOptions> options,
            EmbeddedEdgeVectorMemoryRepository edgeFallback,
            ILogger<QdrantVectorRepository> logger)
        {
            _client = client;
            _options = options.Value;
            _edgeFallback = edgeFallback;
            _logger = logger;
            _collectionName = _options.CollectionName;
        }

        private bool ShouldUsePureEmbedded()
        {
            return _options.IsLocalEdgeMode &&
                   string.Equals(_options.LocalEdgeProvider, "EmbeddedInProcess", StringComparison.OrdinalIgnoreCase);
        }

        public async Task UpsertAsync(Guid memoryId, string embeddingId, IReadOnlyList<float> vector, string? userId, CancellationToken ct)
        {
            if (ShouldUsePureEmbedded())
            {
                await _edgeFallback.UpsertAsync(memoryId, embeddingId, vector, userId, ct);
                return;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));

                await EnsureCollectionExistsAsync(cts.Token);

                var point = new PointStruct
                {
                    Id = new PointId { Uuid = memoryId.ToString() },
                    Vectors = vector.ToArray(),
                    Payload = {
                        ["embeddingId"] = embeddingId,
                        ["userId"] = userId ?? string.Empty
                    }
                };

                await _client.UpsertAsync(_collectionName, new[] { point }, cancellationToken: cts.Token);
                _localContainerFailed = false;

                // In LocalEdge mode, also keep the embedded edge store synchronized
                if (_options.IsLocalEdgeMode)
                {
                    await _edgeFallback.UpsertAsync(memoryId, embeddingId, vector, userId, ct);
                }
            }
            catch (Exception ex)
            {
                if (_options.IsLocalEdgeMode)
                {
                    if (!_localContainerFailed)
                    {
                        _logger.LogInformation("[Qdrant:LocalEdge] Local Qdrant container is not reachable ({Msg}). Seamlessly switching to embedded in-process edge storage.", ex.Message);
                        _localContainerFailed = true;
                    }
                    await _edgeFallback.UpsertAsync(memoryId, embeddingId, vector, userId, ct);
                }
                else
                {
                    _logger.LogWarning(ex, "[Qdrant:Cloud] Failed to upsert vector into collection '{CollectionName}'.", _collectionName);
                }
            }
        }

        public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(IReadOnlyList<float> queryVector, int topK, string? userId, CancellationToken ct)
        {
            if (ShouldUsePureEmbedded() || (_options.IsLocalEdgeMode && _localContainerFailed))
            {
                return await _edgeFallback.SearchAsync(queryVector, topK, userId, ct);
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));

                await EnsureCollectionExistsAsync(cts.Token);

                var filter = new Filter();
                if (!string.IsNullOrEmpty(userId))
                {
                    filter.Must.Add(new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "userId",
                            Match = new Match { Text = userId }
                        }
                    });
                }

#pragma warning disable CS0618
                var searchResult = await _client.SearchAsync(
                    collectionName: _collectionName,
                    vector: queryVector.ToArray(),
                    filter: filter,
                    limit: (ulong)topK,
                    cancellationToken: cts.Token);
#pragma warning restore CS0618

                var results = searchResult.Select(h => new VectorSearchResult(
                    MemoryId: Guid.Parse(h.Id.Uuid),
                    EmbeddingId: h.Payload.TryGetValue("embeddingId", out var val) ? val.StringValue : string.Empty,
                    Score: h.Score
                )).ToList();

                _localContainerFailed = false;
                return results;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                _logger.LogWarning("Qdrant collection '{CollectionName}' not found.", _collectionName);
                if (_options.IsLocalEdgeMode)
                {
                    return await _edgeFallback.SearchAsync(queryVector, topK, userId, ct);
                }
                return Array.Empty<VectorSearchResult>();
            }
            catch (Exception ex)
            {
                if (_options.IsLocalEdgeMode)
                {
                    if (!_localContainerFailed)
                    {
                        _logger.LogInformation("[Qdrant:LocalEdge] Search container unreachable ({Msg}). Querying embedded in-process edge store.", ex.Message);
                        _localContainerFailed = true;
                    }
                    return await _edgeFallback.SearchAsync(queryVector, topK, userId, ct);
                }

                _logger.LogWarning(ex, "[Qdrant:Cloud] Failed to execute vector search in collection '{CollectionName}'.", _collectionName);
                return Array.Empty<VectorSearchResult>();
            }
        }

        public async Task DeleteAsync(Guid memoryId, CancellationToken ct)
        {
            if (ShouldUsePureEmbedded())
            {
                await _edgeFallback.DeleteAsync(memoryId, ct);
                return;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));

                await EnsureCollectionExistsAsync(cts.Token);

                await _client.DeleteAsync(
                    _collectionName,
                    new PointId[] { new PointId { Uuid = memoryId.ToString() } },
                    cancellationToken: cts.Token);

                if (_options.IsLocalEdgeMode)
                {
                    await _edgeFallback.DeleteAsync(memoryId, ct);
                }
            }
            catch (Exception ex)
            {
                if (_options.IsLocalEdgeMode)
                {
                    await _edgeFallback.DeleteAsync(memoryId, ct);
                }
                else
                {
                    _logger.LogWarning(ex, "[Qdrant:Cloud] Failed to delete vector from collection '{CollectionName}'.", _collectionName);
                }
            }
        }

        private async Task EnsureCollectionExistsAsync(CancellationToken ct)
        {
            try
            {
                if (await _client.CollectionExistsAsync(_collectionName, ct))
                {
                    return;
                }

                await _client.CreateCollectionAsync(
                    collectionName: _collectionName,
                    vectorsConfig: new VectorParams
                    {
                        Size = 1536,
                        Distance = Distance.Cosine
                    },
                    quantizationConfig: new QuantizationConfig
                    {
                        Scalar = new ScalarQuantization
                        {
                            Type = QuantizationType.Int8
                        }
                    },
                    cancellationToken: ct);

                _logger.LogInformation("Successfully initialized Qdrant collection {CollectionName} with Scalar Quantization.", _collectionName);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error checking/creating Qdrant collection {CollectionName} (server may be offline).", _collectionName);
                throw;
            }
        }
    }
}
