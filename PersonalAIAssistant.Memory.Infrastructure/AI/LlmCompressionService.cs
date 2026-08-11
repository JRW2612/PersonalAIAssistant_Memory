using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.AI
{
    public class LlmCompressionService : ICompressionService
    {
        private readonly IAIProviderFactory _providerFactory;
        private readonly AiProviderOptions _options;
        private readonly ILogger<LlmCompressionService> _logger;

        public LlmCompressionService(
            IAIProviderFactory providerFactory,
            IOptions<AiProviderOptions> options,
            ILogger<LlmCompressionService> logger)
        {
            _providerFactory = providerFactory;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<CompressionResult> CompressAsync(string text, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new CompressionResult(string.Empty, "none", 0);
            }

            try
            {
                var providerName = !string.IsNullOrEmpty(_options.Default) ? _options.Default : "openai";
                var provider = _providerFactory.GetProvider(providerName);

                var prompt = "You are a text compression engine for an AI memory subsystem. " +
                             "Summarize the provided text in a concise, dense manner while preserving all critical facts, dates, names, and key insights.\n\n" +
                             $"Text to compress:\n{text}";

                var compressedText = await provider.GetResponseAsync(prompt, ct);
                var tokenCountEstimate = compressedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

                return new CompressionResult(
                    Text: compressedText,
                    Model: provider.ProviderName,
                    TokenCount: tokenCountEstimate
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compress text using LLM provider. Returning original text as fallback.");
                return new CompressionResult(text, "fallback-original", text.Length / 4);
            }
        }
    }
}
