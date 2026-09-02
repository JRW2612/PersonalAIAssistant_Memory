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
    public class QdrantVectorRepository : IVectorMemoryRepository
    {
        private readonly QdrantClient _client;
        private readonly ILogger<QdrantVectorRepository> _logger;
        private readonly string _collectionName;

        public QdrantVectorRepository(QdrantClient client, IOptions<QdrantOptions> options, ILogger<QdrantVectorRepository> logger)
        {
            _client = client;
            _logger = logger;
            _collectionName = options.Value.CollectionName;
        }

        public async Task UpsertAsync(Guid memoryId, string embeddingId, IReadOnlyList<float> vector, string? userId, CancellationToken ct)
        {
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upsert vector into Qdrant collection '{CollectionName}' (Qdrant server may be offline).", _collectionName);
            }
        }

        public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(IReadOnlyList<float> queryVector, int topK, string? userId, CancellationToken ct)
        {
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

                return searchResult.Select(h => new VectorSearchResult(
                    MemoryId: Guid.Parse(h.Id.Uuid),
                    EmbeddingId: h.Payload.TryGetValue("embeddingId", out var val) ? val.StringValue : string.Empty,
                    Score: h.Score
                )).ToList();
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                _logger.LogWarning("Qdrant collection '{CollectionName}' not found.", _collectionName);
                return Array.Empty<VectorSearchResult>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to execute vector search in Qdrant collection '{CollectionName}' (Qdrant server may be offline). Returning empty search results.", _collectionName);
                return Array.Empty<VectorSearchResult>();
            }
        }

        public async Task DeleteAsync(Guid memoryId, CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));

                await EnsureCollectionExistsAsync(cts.Token);

                await _client.DeleteAsync(
                    _collectionName,
                    new PointId[] { new PointId { Uuid = memoryId.ToString() } },
                    cancellationToken: cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete vector from Qdrant collection '{CollectionName}' (Qdrant server may be offline).", _collectionName);
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
                _logger.LogWarning(ex, "Error checking/creating Qdrant collection {CollectionName} (Qdrant server may be offline).", _collectionName);
            }
        }
    }
}
