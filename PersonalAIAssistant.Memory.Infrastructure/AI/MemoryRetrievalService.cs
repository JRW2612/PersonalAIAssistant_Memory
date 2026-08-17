using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Infrastructure.AI
{
    public class MemoryRetrievalService : IMemoryRetrievalService
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorMemoryRepository _vectorRepo;
        private readonly IReadModelRepository _readRepo;
        private readonly IRerankingScorer _rerankingScorer;
        private readonly ILogger<MemoryRetrievalService> _logger;
        private readonly IAiMetricsLogger _metricsLogger;

        public MemoryRetrievalService(
            IEmbeddingService embeddingService,
            IVectorMemoryRepository vectorRepo,
            IReadModelRepository readRepo,
            IRerankingScorer rerankingScorer,
            ILogger<MemoryRetrievalService> logger,
            IAiMetricsLogger metricsLogger)
        {
            _embeddingService = embeddingService;
            _vectorRepo = vectorRepo;
            _readRepo = readRepo;
            _rerankingScorer = rerankingScorer;
            _logger = logger;
            _metricsLogger = metricsLogger;
        }

        public async Task<IReadOnlyList<RetrievedMemory>> RetrieveAsync(RetrievalRequest request, CancellationToken ct)
        {
            var start = DateTimeOffset.UtcNow;
            
            // 1. Embed query
            var embedResult = await _embeddingService.GenerateEmbeddingAsync(request.QueryText, ct);
            
            // Record metrics for embedding (mock cost for now)
            _metricsLogger.Record(new AiCallMetrics(
                Provider: embedResult.Provider,
                Model: embedResult.Model,
                Operation: "retrieval-embed",
                PromptTokens: request.QueryText.Length / 4, // naive token approx
                CompletionTokens: 0,
                TotalTokens: request.QueryText.Length / 4,
                EstimatedCostUsd: 0.000001,
                Latency: DateTimeOffset.UtcNow - start,
                WasCacheHit: false,
                WasFallback: false,
                UserId: request.UserId,
                Timestamp: DateTime.UtcNow
            ));

            // 2. Vector search with overfetch
            int overfetchK = request.TopK * 3;
            var searchResults = await _vectorRepo.SearchAsync(embedResult.Vector, overfetchK, request.UserId, ct);

            if (!searchResults.Any())
            {
                return Array.Empty<RetrievedMemory>();
            }

            // 3. Load read models for metadata
            var memoryIds = searchResults.Select(x => x.MemoryId).ToList();
            var readModels = (await _readRepo.GetMemoriesByIdsAsync(memoryIds, ct))
                .ToDictionary(m => m.MemoryId);

            var scoredMemories = new List<RetrievedMemory>();

            // 4. Score and rerank via extracted IRerankingScorer (OCP & SRP)
            foreach (var hit in searchResults)
            {
                if (!readModels.TryGetValue(hit.MemoryId, out var readModel) || readModel.Archived)
                {
                    continue;
                }

                double vectorScore = hit.Score;
                double finalScore = _rerankingScorer.CalculateScore(vectorScore, readModel.CreatedAt, readModel.Importance, out double recencyScore);

                scoredMemories.Add(new RetrievedMemory(
                    hit.MemoryId,
                    readModel.Summary,
                    finalScore,
                    vectorScore,
                    recencyScore,
                    readModel.Importance,
                    readModel.CreatedAt));
            }

            return scoredMemories
                .OrderByDescending(x => x.Score)
                .Take(request.TopK)
                .ToList();
        }
    }
}
