using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using Polly.Registry;

namespace PersonalAIAssistant.Memory.Infrastructure.AI.Anthropic;

/// <summary>Claude Messages API implementation of the shared chat-provider abstraction.</summary>
public sealed class AnthropicChatProvider : IAIProvider
{
    public string ProviderName => "anthropic";

    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly IUserContext _userContext;
    private readonly IProviderTokenService _tokenService;
    private readonly IAiGovernanceValidator _governance;
    private readonly ResiliencePipelineProvider<string> _pipelines;

    public AnthropicChatProvider(
        IHttpClientFactory clients,
        IOptions<AiProviderOptions> options,
        IUserContext userContext,
        IProviderTokenService tokenService,
        IAiGovernanceValidator governance,
        ResiliencePipelineProvider<string> pipelines)
    {
        _http = clients.CreateClient("anthropic");
        _options = options.Value.Anthropic;
        _userContext = userContext;
        _tokenService = tokenService;
        _governance = governance;
        _pipelines = pipelines;
    }

    public async Task<string> GetResponseAsync(string prompt, CancellationToken ct)
    {
        _governance.ValidateProvider(ProviderName);
        return await _pipelines.GetPipeline("AiServiceProtection").ExecuteAsync(async token =>
        {
            var apiKey = _userContext.GetApiKey(ProviderName);
            if (string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(_userContext.UserId))
                apiKey = await _tokenService.GetValidAccessTokenAsync(_userContext.UserId, ProviderName, "ai.generate", token);
            apiKey ??= string.IsNullOrWhiteSpace(_options.ApiKey) ? null : _options.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("No Anthropic API key or connected account is available for this user.");

            using var request = new HttpRequestMessage(HttpMethod.Post, "messages")
            {
                Content = JsonContent.Create(new MessageRequest(_options.Model, _options.MaxTokens,
                    [new Message("user", prompt)], _options.Temperature), options: JsonOptions)
            };
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            request.Headers.TryAddWithoutValidation("X-AI-User-Id", string.IsNullOrWhiteSpace(_userContext.UserId) ? "system" : _userContext.UserId);
            request.Headers.TryAddWithoutValidation("X-AI-Model", _options.Model);
            request.Headers.TryAddWithoutValidation("X-AI-Operation", "chat");

            using var response = await _http.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<MessageResponse>(JsonOptions, token)
                ?? throw new InvalidOperationException("Anthropic returned an empty response.");
            var text = string.Concat(result.Content?.Where(part => part.Type == "text").Select(part => part.Text) ?? []);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Anthropic returned no text content.");
            return text;
        }, ct);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record MessageRequest(string Model, [property: JsonPropertyName("max_tokens")] int MaxTokens,
        IReadOnlyList<Message> Messages, double Temperature);
    private sealed record Message(string Role, string Content);
    private sealed record MessageResponse(IReadOnlyList<ContentPart>? Content);
    private sealed record ContentPart(string Type, string? Text);
}
