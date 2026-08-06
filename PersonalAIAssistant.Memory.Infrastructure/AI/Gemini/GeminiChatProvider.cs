using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Models;
using Polly.Registry;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalAIAssistant.Memory.Infrastructure.AI.Gemini
{
    /// <summary>
    /// IAIProvider implementation that calls the Google Gemini Generative Language API.
    /// Endpoint: https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}
    /// Uses a named HttpClient ("gemini") registered by AddAiProviders().
    /// Wrapped by the "AiServiceProtection" Polly pipeline.
    /// </summary>
    public sealed class GeminiChatProvider : IAIProvider
    {
        public string ProviderName => "gemini";

        private readonly HttpClient _http;
        private readonly GeminiOptions _opts;
        private readonly ResiliencePipelineProvider<string> _polly;
        private readonly ILogger<GeminiChatProvider> _logger;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public GeminiChatProvider(
            IHttpClientFactory httpFactory,
            IOptions<AiProviderOptions> opts,
            ResiliencePipelineProvider<string> polly,
            ILogger<GeminiChatProvider> logger)
        {
            _http   = httpFactory.CreateClient("gemini");
            _opts   = opts.Value.Gemini;
            _polly  = polly;
            _logger = logger;
        }

        public async Task<string> GetResponseAsync(string prompt, CancellationToken ct)
        {
            var pipeline = _polly.GetPipeline("AiServiceProtection");

            return await pipeline.ExecuteAsync(async token =>
            {
                var model   = _opts.ConsolidationModel;
                var url     = $"models/{model}:generateContent?key={_opts.ApiKey}";

                var requestBody = new GenerateRequest
                {
                    Contents = [new Content { Parts = [new Part { Text = prompt }] }],
                    GenerationConfig = new GenerationConfig
                    {
                        MaxOutputTokens = _opts.MaxOutputTokens,
                        Temperature     = _opts.Temperature
                    }
                };

                _logger.LogDebug("[Gemini] Sending request — model: {Model}, prompt length: {Len}",
                    model, prompt.Length);

                var response = await _http.PostAsJsonAsync(url, requestBody, JsonOpts, token);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadFromJsonAsync<GenerateResponse>(JsonOpts, token)
                    ?? throw new InvalidOperationException("[Gemini] Empty response body.");

                var text = body.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
                    ?? throw new InvalidOperationException("[Gemini] No text in candidates.");

                _logger.LogDebug("[Gemini] Response received — length: {Len}", text.Length);

                return text;
            }, ct);
        }

        // ── Request / Response DTOs ──────────────────────────────────────────────

        private sealed class GenerateRequest
        {
            public List<Content> Contents         { get; set; } = [];
            public GenerationConfig? GenerationConfig { get; set; }
        }

        private sealed class Content
        {
            public List<Part> Parts { get; set; } = [];
        }

        private sealed class Part
        {
            public string Text { get; set; } = "";
        }

        private sealed class GenerationConfig
        {
            public int MaxOutputTokens { get; set; }
            public double Temperature  { get; set; }
        }

        private sealed class GenerateResponse
        {
            public List<Candidate>? Candidates { get; set; }
        }

        private sealed class Candidate
        {
            public Content? Content { get; set; }
        }
    }
}
