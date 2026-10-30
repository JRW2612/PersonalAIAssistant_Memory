using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.AI.Gemini;
using PersonalAIAssistant.Memory.Infrastructure.AI.OpenAi;

namespace PersonalAIAssistant.Memory.Infrastructure.AI
{
    /// <summary>
    /// Composite router that delegates embedding operations to Google Gemini or OpenAI/CLIP based on
    /// user context, preferred provider, or configuration.
    /// </summary>
    public class MultimodalEmbeddingRouter : IEmbeddingService
    {
        private readonly OpenAiEmbeddingService _openAiEmbedding;
        private readonly GeminiMultimodalEmbeddingService _geminiEmbedding;
        private readonly AiProviderOptions _options;
        private readonly IUserContext _userContext;
        private readonly ILogger<MultimodalEmbeddingRouter> _logger;

        public MultimodalEmbeddingRouter(
            OpenAiEmbeddingService openAiEmbedding,
            GeminiMultimodalEmbeddingService geminiEmbedding,
            IOptions<AiProviderOptions> options,
            IUserContext userContext,
            ILogger<MultimodalEmbeddingRouter> logger)
        {
            _openAiEmbedding = openAiEmbedding;
            _geminiEmbedding = geminiEmbedding;
            _options = options.Value;
            _userContext = userContext;
            _logger = logger;
        }

        private bool ShouldUseGemini()
        {
            var preferred = _userContext.PreferredProvider;
            if (string.Equals(preferred, "gemini", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(preferred, "openai", StringComparison.OrdinalIgnoreCase)) return false;

            return string.Equals(_options.Default, "gemini", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken ct)
        {
            if (ShouldUseGemini())
            {
                try { return await _geminiEmbedding.GenerateEmbeddingAsync(text, ct); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[EmbeddingRouter] Gemini text embedding failed. Falling back to OpenAI.");
                }
            }
            return await _openAiEmbedding.GenerateEmbeddingAsync(text, ct);
        }

        public async Task<EmbeddingResult> GenerateVisualEmbeddingAsync(VisualContent visualContent, CancellationToken ct)
        {
            if (ShouldUseGemini())
            {
                try { return await _geminiEmbedding.GenerateVisualEmbeddingAsync(visualContent, ct); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[EmbeddingRouter] Gemini visual embedding failed. Falling back to OpenAI/CLIP.");
                }
            }
            return await _openAiEmbedding.GenerateVisualEmbeddingAsync(visualContent, ct);
        }

        public async Task<EmbeddingResult> GenerateMultimodalEmbeddingAsync(MultimodalEmbeddingRequest request, CancellationToken ct)
        {
            if (ShouldUseGemini())
            {
                try { return await _geminiEmbedding.GenerateMultimodalEmbeddingAsync(request, ct); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[EmbeddingRouter] Gemini multimodal embedding failed. Falling back to OpenAI/CLIP.");
                }
            }
            return await _openAiEmbedding.GenerateMultimodalEmbeddingAsync(request, ct);
        }
    }
}
