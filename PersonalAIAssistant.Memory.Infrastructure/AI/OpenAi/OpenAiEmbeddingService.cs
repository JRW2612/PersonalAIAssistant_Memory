using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.AI.OpenAi
{
    public class OpenAiEmbeddingService : IEmbeddingService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly OpenAiOptions _options;
        private readonly ILogger<OpenAiEmbeddingService> _logger;

        public OpenAiEmbeddingService(
            IHttpClientFactory httpClientFactory,
            IOptions<OpenAiOptions> options,
            ILogger<OpenAiEmbeddingService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey) || _options.ApiKey == "sk-placeholder")
            {
                _logger.LogWarning("OpenAI API key not configured. Returning deterministic dummy embedding vector for local testing.");
                return GenerateDummyEmbedding(text);
            }

            var client = _httpClientFactory.CreateClient("openai");
            const string modelName = "text-embedding-3-small";

            var requestBody = new
            {
                input = text,
                model = modelName
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings")
            {
                Content = JsonContent.Create(requestBody)
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

            try
            {
                var response = await client.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken: ct);
                var vector = json?.Data?.FirstOrDefault()?.Embedding;

                if (vector != null && vector.Length > 0)
                {
                    return new EmbeddingResult(
                        EmbeddingId: Guid.NewGuid().ToString(),
                        Vector: vector,
                        Provider: "OpenAI",
                        Model: modelName
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to call OpenAI Embeddings API. Falling back to dummy vector.");
            }

            return GenerateDummyEmbedding(text);
        }

        private static EmbeddingResult GenerateDummyEmbedding(string text)
        {
            var vector = new float[1536];
            var hash = text.GetHashCode();
            var rand = new Random(hash);
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)(rand.NextDouble() * 2.0 - 1.0);
            }

            return new EmbeddingResult(
                EmbeddingId: Guid.NewGuid().ToString(),
                Vector: vector,
                Provider: "MockFallback",
                Model: "dummy-1536"
            );
        }

        private class OpenAiEmbeddingResponse
        {
            [JsonPropertyName("data")]
            public List<EmbeddingData>? Data { get; set; }
        }

        private class EmbeddingData
        {
            [JsonPropertyName("embedding")]
            public float[]? Embedding { get; set; }
        }
    }
}
