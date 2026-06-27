using PersonalAIAssistant.Memory.Core.DTOs;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    public interface ICompressionService
    {
        /// <summary>
        /// Compress or summarize text into a smaller representation.
        /// </summary>
        Task<CompressionResult> CompressAsync(string text, CancellationToken ct);
    }


}
