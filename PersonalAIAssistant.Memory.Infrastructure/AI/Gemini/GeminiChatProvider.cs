using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
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
        private readonly IAiMetricsLogger _metrics;
        private readonly IAiGovernanceValidator _governance;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public GeminiChatProvider(
            IHttpClientFactory httpFactory,
            IOptions<AiProviderOptions> opts,
            ResiliencePipelineProvider<string> polly,
            ILogger<GeminiChatProvider> logger,
            IAiMetricsLogger metrics,
            IAiGovernanceValidator governance)
        {
            _http   = httpFactory.CreateClient("gemini");
            _opts   = opts.Value.Gemini;
            _polly  = polly;
            _logger = logger;
            _metrics = metrics;
            _governance = governance;
        }

        public async Task<string> GetResponseAsync(string prompt, CancellationToken ct)
        {
            _governance.ValidateProvider("gemini");
            var pipeline = _polly.GetPipeline("AiServiceProtection");

            return await pipeline.ExecuteAsync(async token =>
            {
                var model   = _opts.ConsolidationModel;
                var url     = $"models/{model}:generateContent";

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

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(requestBody, options: JsonOpts)
                };
                requestMessage.Headers.TryAddWithoutValidation("x-goog-api-key", _opts.ApiKey);
                foreach (var header in _governance.GetComplianceHeaders())
                {
                    requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                var start = DateTimeOffset.UtcNow;
                var response = await _http.SendAsync(requestMessage, token);
                response.EnsureSuccessStatusCode();
                var latency = DateTimeOffset.UtcNow - start;

                var body = await response.Content.ReadFromJsonAsync<GenerateResponse>(JsonOpts, token)
                    ?? throw new InvalidOperationException("[Gemini] Empty response body.");

                var text = body.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
                    ?? throw new InvalidOperationException("[Gemini] No text in candidates.");

                var promptTokens = body.UsageMetadata?.PromptTokenCount ?? 0;
                var completionTokens = body.UsageMetadata?.CandidatesTokenCount ?? 0;
                var totalTokens = body.UsageMetadata?.TotalTokenCount ?? 0;

                var cost = (promptTokens * 0.000075 / 1000.0) + (completionTokens * 0.00030 / 1000.0);

                _metrics.Record(new AiCallMetrics(
                    Provider: ProviderName,
                    Model: model,
                    Operation: "chat",
                    PromptTokens: promptTokens,
                    CompletionTokens: completionTokens,
                    TotalTokens: totalTokens,
                    EstimatedCostUsd: cost,
                    Latency: latency,
                    WasCacheHit: false,
                    WasFallback: false,
                    UserId: null,
                    Timestamp: DateTime.UtcNow
                ));

                return text;
            }, ct);
        }

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
            public UsageMetadata? UsageMetadata { get; set; }
        }

        private sealed class Candidate
        {
            public Content? Content { get; set; }
        }

        private sealed class UsageMetadata
        {
            public int PromptTokenCount { get; set; }
            public int CandidatesTokenCount { get; set; }
            public int TotalTokenCount { get; set; }
        }
    }
}
