namespace PersonalAIAssistant.Memory.Core.DTOs
{
    /// <summary>
    /// Result of generating a vector embedding for a piece of text.
    /// </summary>
    public record EmbeddingResult(string EmbeddingId, IReadOnlyList<float> Vector, string Provider, string Model);
}

