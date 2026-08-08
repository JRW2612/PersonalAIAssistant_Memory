using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

        public async Task UpsertAsync(Guid memoryId, string embeddingId, IReadOnlyList<float> vector, CancellationToken ct)
        {
            var point = new PointStruct
            {
                Id = new PointId { Uuid = memoryId.ToString() },
                Vectors = vector.ToArray(),
                Payload = {
                    ["embeddingId"] = embeddingId
                }
            };

            await _client.UpsertAsync(_collectionName, new[] { point }, cancellationToken: ct);
        }

        public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(IReadOnlyList<float> queryVector, int topK, CancellationToken ct)
        {
            try
            {
#pragma warning disable CS0618 // Type or member is obsolete
                var searchResult = await _client.SearchAsync(
                    collectionName: _collectionName,
                    vector: queryVector.ToArray(),
                    limit: (ulong)topK,
                    cancellationToken: ct);
#pragma warning restore CS0618 // Type or member is obsolete

                return searchResult.Select(h => new VectorSearchResult(
                    MemoryId: Guid.Parse(h.Id.Uuid),
                    EmbeddingId: h.Payload.TryGetValue("embeddingId", out var val) ? val.StringValue : string.Empty,
                    Score: h.Score
                )).ToList();
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                _logger.LogWarning("Qdrant collection {CollectionName} not found.", _collectionName);
                return Array.Empty<VectorSearchResult>();
            }
        }

        public async Task DeleteAsync(Guid memoryId, CancellationToken ct)
        {
            await _client.DeleteAsync(
                _collectionName,
                new PointId[] { new PointId { Uuid = memoryId.ToString() } },
                cancellationToken: ct);
        }
    }
}
