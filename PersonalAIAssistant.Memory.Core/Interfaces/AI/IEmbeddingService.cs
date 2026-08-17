using PersonalAIAssistant.Memory.Core.DTOs;

namespace PersonalAIAssistant.Memory.Core.Interfaces.AI
{
    public interface IEmbeddingService
    {
        Task<EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken ct);
    }
}
