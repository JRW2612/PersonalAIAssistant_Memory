using PersonalAIAssistant.Memory.Core.DTOs;

namespace PersonalAIAssistant.Memory.Core.Interfaces.AI
{
    public interface ICompressionService
    {
        Task<CompressionResult> CompressAsync(string text, CancellationToken ct);
    }
}
