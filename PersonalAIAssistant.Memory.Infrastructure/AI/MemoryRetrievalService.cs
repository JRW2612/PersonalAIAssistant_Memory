using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.AI
{
    public class MemoryRetrievalService : IMemoryRetrievalService
    {
        private const int MaximumTopK = 100;
        private const double ReciprocalRankOffset = 60;
        private const double VectorRankWeight = 0.65;
        private const double LexicalRankWeight = 0.35;
        private static readonly Regex TokenPattern = new("[\\p{L}\\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.UserId) || request.TopK <= 0)
                return Array.Empty<RetrievedMemory>();

            var topK = Math.Min(request.TopK, MaximumTopK);
            var overfetchK = topK * 3;
            var start = DateTimeOffset.UtcNow;
            var queryTerms = Tokenize(request.QueryText).Distinct(StringComparer.Ordinal).ToList();

            // Read the caller's own decrypted read models for lexical candidate discovery.
            // This path also works when vector generation is temporarily unavailable.
            var userMemories = queryTerms.Count > 0
                ? (await _readRepo.GetMemoriesByUserAsync(request.UserId, ct))
                    .Where(memory => !memory.Archived)
                    .ToList()
                : new List<MemoryReadModel>();

            var lexicalScores = ScoreLexically(userMemories, queryTerms, request.QueryText);
            // Generate an embedding when possible. A text query can still return lexical matches
            // if the embedding provider is unavailable.
            EmbeddingResult? embedResult = null;
            try
            {
                if (request.VisualQuery != null)
                {
                    embedResult = await _embeddingService.GenerateMultimodalEmbeddingAsync(
                        new MultimodalEmbeddingRequest(
                            text: string.IsNullOrWhiteSpace(request.QueryText) ? null : request.QueryText,
                            visual: request.VisualQuery),
                        ct);
                }
                else if (!string.IsNullOrWhiteSpace(request.QueryText))
                {
                    embedResult = await _embeddingService.GenerateEmbeddingAsync(request.QueryText, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Embedding generation failed; continuing with lexical memory retrieval for user {UserId}", request.UserId);
            }

            IReadOnlyList<VectorSearchResult> searchResults = Array.Empty<VectorSearchResult>();
            if (embedResult != null)
            {
                _metricsLogger.Record(new AiCallMetrics(
                    Provider: embedResult.Provider,
                    Model: embedResult.Model,
                    Operation: "retrieval-embed",
                    PromptTokens: (request.QueryText?.Length ?? 0) / 4,
                    CompletionTokens: 0,
                    TotalTokens: (request.QueryText?.Length ?? 0) / 4,
                    EstimatedCostUsd: 0.000001,
                    Latency: DateTimeOffset.UtcNow - start,
                    WasCacheHit: false,
                    WasFallback: false,
                    UserId: request.UserId,
                    Timestamp: DateTime.UtcNow
                ));

                try
                {
                    searchResults = await _vectorRepo.SearchAsync(embedResult.Vector, overfetchK, request.UserId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Vector search failed; continuing with lexical memory retrieval for user {UserId}", request.UserId);
                }
            }

            // Union vector and lexical candidates. Read-model ownership is checked again before
            // returning results, even though both candidate sources are user-scoped.
            var readModels = userMemories.ToDictionary(memory => memory.MemoryId);
            var missingIds = searchResults.Select(hit => hit.MemoryId)
                .Where(id => !readModels.ContainsKey(id))
                .Distinct()
                .ToList();
            if (missingIds.Count > 0)
            {
                foreach (var memory in await _readRepo.GetMemoriesByIdsAsync(missingIds, ct))
                    readModels[memory.MemoryId] = memory;
            }

            var vectorRanks = searchResults
                .Select((hit, index) => (hit.MemoryId, Rank: index + 1, hit.Score))
                .GroupBy(hit => hit.MemoryId)
                .ToDictionary(group => group.Key, group => group.First());

            var lexicalRanked = lexicalScores
                .OrderByDescending(entry => entry.Value)
                .Take(overfetchK)
                .ToList();
            var lexicalRanks = lexicalRanked
                .Select((entry, index) => (MemoryId: entry.Key, Rank: index + 1))
                .ToDictionary(entry => entry.MemoryId, entry => entry.Rank);

            var scoredMemories = new List<RetrievedMemory>();

            foreach (var memoryId in vectorRanks.Keys.Union(lexicalRanks.Keys))
            {
                if (!readModels.TryGetValue(memoryId, out var readModel)
                    || readModel.Archived
                    || !string.Equals(readModel.UserId, request.UserId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hasVector = vectorRanks.TryGetValue(memoryId, out var vectorHit);
                var hasLexical = lexicalRanks.TryGetValue(memoryId, out var lexicalRank);
                var rankScore = (hasVector ? VectorRankWeight / (ReciprocalRankOffset + vectorHit.Rank) : 0)
                    + (hasLexical ? LexicalRankWeight / (ReciprocalRankOffset + lexicalRank) : 0);
                var fusedRelevance = Math.Clamp(rankScore * (ReciprocalRankOffset + 1), 0, 1);
                var vectorScore = hasVector ? vectorHit.Score : 0;
                var lexicalScore = lexicalScores.TryGetValue(memoryId, out var score) ? score : 0;
                var finalScore = _rerankingScorer.CalculateScore(fusedRelevance, readModel.CreatedAt, readModel.Importance, out var recencyScore);

                scoredMemories.Add(new RetrievedMemory(
                    memoryId,
                    readModel.Summary,
                    finalScore,
                    vectorScore,
                    recencyScore,
                    readModel.Importance,
                    readModel.CreatedAt,
                    LexicalScore: lexicalScore));
            }

            return scoredMemories
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .ToList();
        }

        private static Dictionary<Guid, double> ScoreLexically(
            IReadOnlyList<MemoryReadModel> memories,
            IReadOnlyList<string> queryTerms,
            string queryText)
        {
            if (memories.Count == 0 || queryTerms.Count == 0)
                return new Dictionary<Guid, double>();

            var documents = memories
                .Select(memory => new
                {
                    Memory = memory,
                    Terms = Tokenize(memory.Summary)
                })
                .Where(document => document.Terms.Count > 0)
                .ToList();
            if (documents.Count == 0) return new Dictionary<Guid, double>();

            var averageLength = documents.Average(document => document.Terms.Count);
            var documentFrequencies = queryTerms.ToDictionary(
                term => term,
                term => documents.Count(document => document.Terms.Contains(term, StringComparer.Ordinal)));
            var normalizedQuery = string.Join(" ", queryTerms);
            var scores = new Dictionary<Guid, double>();

            foreach (var document in documents)
            {
                var score = 0d;
                foreach (var term in queryTerms)
                {
                    var frequency = document.Terms.Count(candidate => candidate == term);
                    if (frequency == 0) continue;

                    var documentFrequency = documentFrequencies[term];
                    var inverseDocumentFrequency = Math.Log(1 + (documents.Count - documentFrequency + 0.5) / (documentFrequency + 0.5));
                    const double k1 = 1.2;
                    const double b = 0.75;
                    var lengthNormalization = 1 - b + b * document.Terms.Count / averageLength;
                    score += inverseDocumentFrequency * frequency * (k1 + 1) / (frequency + k1 * lengthNormalization);
                }

                if (score > 0 && !string.IsNullOrWhiteSpace(queryText))
                {
                    var normalizedText = string.Join(" ", document.Terms);
                    if (normalizedText.Contains(normalizedQuery, StringComparison.Ordinal))
                        score += 0.5;
                    scores[document.Memory.MemoryId] = score;
                }
            }

            var maxScore = scores.Count == 0 ? 0 : scores.Values.Max();
            return maxScore <= 0
                ? new Dictionary<Guid, double>()
                : scores.ToDictionary(entry => entry.Key, entry => entry.Value / maxScore);
        }

        private static List<string> Tokenize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return TokenPattern.Matches(text)
                .Select(match => match.Value.ToLowerInvariant())
                .Where(token => token.Length > 1)
                .ToList();
        }
    }
}
