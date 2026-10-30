using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.AI.Gemini
{
    /// <summary>
    /// Multimodal Embedding service utilizing Google Gemini's embedContent API.
    /// Supports text, visual content (camera snapshots, whiteboard drawings), and combined multimodal embeddings.
    /// </summary>
    public class GeminiMultimodalEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _http;
        private readonly GeminiOptions _opts;
        private readonly IUserContext _userContext;
        private readonly IProviderTokenService? _tokenService;
        private readonly ILogger<GeminiMultimodalEmbeddingService> _logger;

        private const string DefaultEmbeddingModel = "text-embedding-004";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public GeminiMultimodalEmbeddingService(
            IHttpClientFactory httpFactory,
            IOptions<AiProviderOptions> opts,
            IUserContext userContext,
            ILogger<GeminiMultimodalEmbeddingService> logger,
            IProviderTokenService? tokenService = null)
        {
            _http = httpFactory.CreateClient("gemini");
            _opts = opts.Value.Gemini;
            _userContext = userContext;
            _logger = logger;
            _tokenService = tokenService;
        }

        public async Task<EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken ct)
        {
            return await CallGeminiEmbeddingApiAsync(textParts: new[] { text }, visualParts: null, ct);
        }

        public async Task<EmbeddingResult> GenerateVisualEmbeddingAsync(VisualContent visualContent, CancellationToken ct)
        {
            return await CallGeminiEmbeddingApiAsync(
                textParts: string.IsNullOrWhiteSpace(visualContent.Caption) ? null : new[] { visualContent.Caption },
                visualParts: new[] { visualContent },
                ct);
        }

        public async Task<EmbeddingResult> GenerateMultimodalEmbeddingAsync(MultimodalEmbeddingRequest request, CancellationToken ct)
        {
            var textParts = !string.IsNullOrWhiteSpace(request.Text) ? new[] { request.Text } : null;
            var visualParts = request.Visual != null && request.Visual.Data.Length > 0 ? new[] { request.Visual } : null;

            return await CallGeminiEmbeddingApiAsync(textParts, visualParts, ct);
        }

        private async Task<EmbeddingResult> CallGeminiEmbeddingApiAsync(
            IEnumerable<string>? textParts,
            IEnumerable<VisualContent>? visualParts,
            CancellationToken ct)
        {
            var parts = new List<object>();

            if (textParts != null)
            {
                foreach (var text in textParts)
                {
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        parts.Add(new { text });
                    }
                }
            }

            if (visualParts != null)
            {
                foreach (var v in visualParts)
                {
                    if (v.Data.Length > 0)
                    {
                        parts.Add(new
                        {
                            inlineData = new
                            {
                                mimeType = v.MimeType,
                                data = Convert.ToBase64String(v.Data)
                            }
                        });
                    }
                }
            }

            if (parts.Count == 0)
            {
                parts.Add(new { text = "empty" });
            }

            var apiKey = _userContext.GetApiKey("gemini");
            if (string.IsNullOrWhiteSpace(apiKey) && _tokenService != null && !string.IsNullOrWhiteSpace(_userContext.UserId))
            {
                apiKey = await _tokenService.GetValidAccessTokenAsync(_userContext.UserId, "gemini", "ai.generate", ct);
            }
            apiKey ??= (!string.IsNullOrWhiteSpace(_opts.ApiKey) ? _opts.ApiKey : null);

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var model = DefaultEmbeddingModel;
                var url = $"models/{model}:embedContent?key={Uri.EscapeDataString(apiKey)}";

                var requestBody = new
                {
                    model = $"models/{model}",
                    content = new
                    {
                        parts
                    }
                };

                try
                {
                    var response = await _http.PostAsJsonAsync(url, requestBody, JsonOpts, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadFromJsonAsync<GeminiEmbedResponse>(JsonOpts, ct);
                        var values = json?.Embedding?.Values;
                        if (values != null && values.Length > 0)
                        {
                            return new EmbeddingResult(
                                EmbeddingId: Guid.NewGuid().ToString(),
                                Vector: values,
                                Provider: "Google-Gemini",
                                Model: model
                            );
                        }
                    }
                    else
                    {
                        var err = await response.Content.ReadAsStringAsync(ct);
                        _logger.LogWarning("[GeminiEmbed] Failed with status {StatusCode}: {Error}", response.StatusCode, err);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[GeminiEmbed] Request exception. Falling back to deterministic multimodal vector.");
                }
            }

            // Deterministic offline projection fallback
            return GenerateDeterministicMultimodalFallback(textParts, visualParts);
        }

        private static EmbeddingResult GenerateDeterministicMultimodalFallback(
            IEnumerable<string>? textParts,
            IEnumerable<VisualContent>? visualParts)
        {
            var vector = new float[768];
            using var sha = SHA256.Create();
            var seedBytes = new List<byte>();

            if (textParts != null)
            {
                foreach (var t in textParts)
                {
                    seedBytes.AddRange(System.Text.Encoding.UTF8.GetBytes(t));
                }
            }

            if (visualParts != null)
            {
                foreach (var v in visualParts)
                {
                    seedBytes.AddRange(v.Data.Take(64));
                    seedBytes.AddRange(System.Text.Encoding.UTF8.GetBytes(v.Modality.ToString()));
                }
            }

            if (seedBytes.Count == 0)
            {
                seedBytes.AddRange(new byte[] { 42, 42 });
            }

            var hash = sha.ComputeHash(seedBytes.ToArray());
            var seed = BitConverter.ToInt32(hash, 0);
            var rand = new Random(seed);

            float sumSq = 0f;
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)(rand.NextDouble() * 2.0 - 1.0);
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

            return new EmbeddingResult(
                EmbeddingId: Guid.NewGuid().ToString(),
                Vector: vector,
                Provider: "Gemini-MultimodalFallback",
                Model: "gemini-embed-simulated-768"
            );
        }

        private class GeminiEmbedResponse
        {
            [JsonPropertyName("embedding")]
            public GeminiEmbeddingValues? Embedding { get; set; }
        }

        private class GeminiEmbeddingValues
        {
            [JsonPropertyName("values")]
            public float[]? Values { get; set; }
        }
    }
}
