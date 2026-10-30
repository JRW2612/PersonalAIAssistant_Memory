using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Core.Interfaces.AI
{
    /// <summary>
    /// Service for generating dense vector embeddings from text, visual content (camera snapshots, whiteboard drawings),
    /// and unified multimodal inputs.
    /// </summary>
    public interface IEmbeddingService
    {
        /// <summary>Generates a vector embedding from conversational or unstructured text.</summary>
        Task<EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken ct);

        /// <summary>Generates a visual vector embedding from images, camera snapshots, or whiteboard drawings.</summary>
        Task<EmbeddingResult> GenerateVisualEmbeddingAsync(VisualContent visualContent, CancellationToken ct);

        /// <summary>Generates a joint multimodal embedding combining text context and visual observations.</summary>
        Task<EmbeddingResult> GenerateMultimodalEmbeddingAsync(MultimodalEmbeddingRequest request, CancellationToken ct);
    }
}
