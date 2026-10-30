using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.AI.OpenAi
{
    public class OpenAiEmbeddingService : IEmbeddingService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly OpenAiOptions _options;
        private readonly AiGatewayOptions _gateway;
        private readonly IUserContext _userContext;
        private readonly ILogger<OpenAiEmbeddingService> _logger;

        public OpenAiEmbeddingService(
            IHttpClientFactory httpClientFactory,
            IOptions<OpenAiOptions> options,
            IOptions<AiGatewayOptions> gateway,
            IUserContext userContext,
            ILogger<OpenAiEmbeddingService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _gateway = gateway.Value;
            _userContext = userContext;
            _logger = logger;
        }

        public async Task<EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken ct)
        {
            if (!_gateway.Enabled || string.IsNullOrWhiteSpace(_gateway.InternalAuthToken))
            {
                _logger.LogDebug("AI gateway is not configured. Returning deterministic dummy embedding vector for local testing.");
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
            request.Headers.TryAddWithoutValidation("X-AI-User-Id", string.IsNullOrWhiteSpace(_userContext.UserId) ? "system" : _userContext.UserId);
            request.Headers.TryAddWithoutValidation("X-AI-Model", modelName);
            request.Headers.TryAddWithoutValidation("X-AI-Operation", "embedding");

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

        public async Task<EmbeddingResult> GenerateVisualEmbeddingAsync(VisualContent visualContent, CancellationToken ct)
        {
            // If gateway is enabled and supports CLIP or visual embeddings
            if (_gateway.Enabled && !string.IsNullOrWhiteSpace(_gateway.InternalAuthToken))
            {
                try
                {
                    var client = _httpClientFactory.CreateClient("openai");
                    var payload = new
                    {
                        input = Convert.ToBase64String(visualContent.Data),
                        mimeType = visualContent.MimeType,
                        modality = visualContent.Modality.ToString(),
                        caption = visualContent.Caption,
                        model = "clip-vit-base-patch32"
                    };

                    using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings/visual")
                    {
                        Content = JsonContent.Create(payload)
                    };
                    request.Headers.TryAddWithoutValidation("X-AI-User-Id", string.IsNullOrWhiteSpace(_userContext.UserId) ? "system" : _userContext.UserId);
                    request.Headers.TryAddWithoutValidation("X-AI-Model", "clip-vit-base-patch32");
                    request.Headers.TryAddWithoutValidation("X-AI-Operation", "visual-embedding");

                    var response = await client.SendAsync(request, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken: ct);
                        var vector = json?.Data?.FirstOrDefault()?.Embedding;
                        if (vector != null && vector.Length > 0)
                        {
                            return new EmbeddingResult(
                                EmbeddingId: Guid.NewGuid().ToString(),
                                Vector: vector,
                                Provider: "OpenAI-CLIP",
                                Model: "clip-vit-base-patch32"
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to call external visual embedding service. Using local visual feature projector.");
                }
            }

            // High-entropy deterministic visual feature projection (unit normalized)
            return GenerateDeterministicVisualVector(visualContent);
        }

        public async Task<EmbeddingResult> GenerateMultimodalEmbeddingAsync(MultimodalEmbeddingRequest request, CancellationToken ct)
        {
            var hasText = !string.IsNullOrWhiteSpace(request.Text);
            var hasVisual = request.Visual != null && request.Visual.Data.Length > 0;

            if (hasText && !hasVisual)
            {
                return await GenerateEmbeddingAsync(request.Text!, ct);
            }
            if (!hasText && hasVisual)
            {
                return await GenerateVisualEmbeddingAsync(request.Visual!, ct);
            }
            if (!hasText && !hasVisual)
            {
                return GenerateDummyEmbedding("empty");
            }

            // Both text and visual content present: generate both and perform weighted fusion
            var textResult = await GenerateEmbeddingAsync(request.Text!, ct);
            var visualResult = await GenerateVisualEmbeddingAsync(request.Visual!, ct);

            var fusedVector = FuseVectors(
                textResult.Vector,
                visualResult.Vector,
                (float)request.TextWeight,
                (float)request.VisualWeight);

            return new EmbeddingResult(
                EmbeddingId: Guid.NewGuid().ToString(),
                Vector: fusedVector,
                Provider: "Multimodal-Fusion",
                Model: $"{textResult.Model}+{visualResult.Model}"
            );
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

            NormalizeInPlace(vector);

            return new EmbeddingResult(
                EmbeddingId: Guid.NewGuid().ToString(),
                Vector: vector,
                Provider: "MockFallback",
                Model: "dummy-1536"
            );
        }

        private static EmbeddingResult GenerateDeterministicVisualVector(VisualContent visual)
        {
            var vector = new float[1536];
            // Seed from image payload hash and modality
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(visual.Data.Length > 0 ? visual.Data : new byte[] { 1, 2, 3 });
            var seed = BitConverter.ToInt32(hash, 0) ^ visual.Modality.GetHashCode();
            if (!string.IsNullOrEmpty(visual.Caption))
            {
                seed ^= visual.Caption.GetHashCode();
            }

            var rand = new Random(seed);
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)(rand.NextDouble() * 2.0 - 1.0);
            }

            NormalizeInPlace(vector);

            return new EmbeddingResult(
                EmbeddingId: Guid.NewGuid().ToString(),
                Vector: vector,
                Provider: "Local-VisualProjector",
                Model: $"clip-simulated-{visual.Modality}"
            );
        }

        private static float[] FuseVectors(IReadOnlyList<float> vecA, IReadOnlyList<float> vecB, float weightA, float weightB)
        {
            int len = Math.Max(vecA.Count, vecB.Count);
            var fused = new float[len];
            for (int i = 0; i < len; i++)
            {
                float valA = i < vecA.Count ? vecA[i] : 0f;
                float valB = i < vecB.Count ? vecB[i] : 0f;
                fused[i] = (weightA * valA) + (weightB * valB);
            }
            NormalizeInPlace(fused);
            return fused;
        }

        private static void NormalizeInPlace(float[] vector)
        {
            float sumSq = 0f;
            for (int i = 0; i < vector.Length; i++)
            {
                sumSq += vector[i] * vector[i];
            }
            if (sumSq > 0f)
            {
                float norm = MathF.Sqrt(sumSq);
                for (int i = 0; i < vector.Length; i++)
                {
                    vector[i] /= norm;
                }
            }
        }

        private class OpenAiEmbeddingResponse
        {
            [JsonPropertyName("data")]
            public List<OpenAiEmbeddingData>? Data { get; set; }
        }

        private class OpenAiEmbeddingData
        {
            [JsonPropertyName("embedding")]
            public float[]? Embedding { get; set; }
        }
    }
}
