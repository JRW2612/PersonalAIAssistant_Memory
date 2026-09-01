using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;
using Polly.Registry;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalAIAssistant.Memory.Infrastructure.AI.OpenAi
{
    /// <summary>
    /// IAIProvider implementation that calls the OpenAI Chat Completions API via direct HTTP.
    /// Uses a named HttpClient ("openai") registered by AddAiProviders().
    /// Wrapped by the "AiServiceProtection" Polly pipeline (circuit-breaker + retry).
    /// </summary>
    public sealed class OpenAiChatProvider : IAIProvider
    {
        public string ProviderName => "openai";

        private readonly HttpClient _http;
        private readonly OpenAiOptions _opts;
        private readonly ResiliencePipelineProvider<string> _polly;
        private readonly ILogger<OpenAiChatProvider> _logger;
        private readonly IAiMetricsLogger _metrics;
        private readonly IAiGovernanceValidator _governance;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public OpenAiChatProvider(
            IHttpClientFactory httpFactory,
            IOptions<AiProviderOptions> opts,
            ResiliencePipelineProvider<string> polly,
            ILogger<OpenAiChatProvider> logger,
            IAiMetricsLogger metrics,
            IAiGovernanceValidator governance)
        {
            _http   = httpFactory.CreateClient("openai");
            _opts   = opts.Value.OpenAi;
            _polly  = polly;
            _logger = logger;
            _metrics = metrics;
            _governance = governance;
        }

        public async Task<string> GetResponseAsync(string prompt, CancellationToken ct)
        {
            _governance.ValidateProvider("openai");
            var pipeline = _polly.GetPipeline("AiServiceProtection");

            return await pipeline.ExecuteAsync(async token =>
            {
                var request = new ChatRequest
                {
                    Model    = _opts.ConsolidationModel,
                    Messages = [new ChatMessage { Role = "user", Content = prompt }],
                    MaxTokens   = _opts.MaxTokens,
                    Temperature = _opts.Temperature
                };

                _logger.LogDebug("[OpenAI] Sending request — model: {Model}, prompt length: {Len}",
                    request.Model, prompt.Length);

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                {
                    Content = JsonContent.Create(request, options: JsonOpts)
                };
                httpRequest.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
                foreach (var header in _governance.GetComplianceHeaders())
                {
                    httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                var start = DateTimeOffset.UtcNow;
                var response = await _http.SendAsync(httpRequest, token);
                response.EnsureSuccessStatusCode();
                var latency = DateTimeOffset.UtcNow - start;

                var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOpts, token)
                    ?? throw new InvalidOperationException("[OpenAI] Empty response body.");

                var text = body.Choices?.FirstOrDefault()?.Message?.Content
                    ?? throw new InvalidOperationException("[OpenAI] No content in choices.");

                var promptTokens = body.Usage?.PromptTokens ?? 0;
                var completionTokens = body.Usage?.CompletionTokens ?? 0;
                var totalTokens = body.Usage?.TotalTokens ?? 0;

                var cost = (promptTokens * 0.00015 / 1000.0) + (completionTokens * 0.00060 / 1000.0);

                _metrics.Record(new AiCallMetrics(
                    Provider: ProviderName,
                    Model: request.Model,
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

        private sealed class ChatRequest
        {
            public string Model { get; set; } = "";
            public List<ChatMessage> Messages { get; set; } = [];
            [JsonPropertyName("max_tokens")]
            public int MaxTokens { get; set; }
            public double Temperature { get; set; }
        }

        private sealed class ChatMessage
        {
            public string Role    { get; set; } = "";
            public string Content { get; set; } = "";
        }

        private sealed class ChatResponse
        {
            public List<Choice>? Choices { get; set; }
            public UsageInfo?    Usage   { get; set; }
        }

        private sealed class Choice
        {
            public ChatMessage? Message { get; set; }
        }

        private sealed class UsageInfo
        {
            [JsonPropertyName("prompt_tokens")]
            public int PromptTokens { get; set; }
            [JsonPropertyName("completion_tokens")]
            public int CompletionTokens { get; set; }
            [JsonPropertyName("total_tokens")]
            public int TotalTokens { get; set; }
        }
    }
}
