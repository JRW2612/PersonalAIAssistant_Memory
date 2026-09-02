namespace PersonalAIAssistant.Memory.Core.DTOs
{
    /// <summary>
    /// A single match returned from a semantic (vector similarity) search.
    /// </summary>
    public record VectorSearchResult(Guid MemoryId, string EmbeddingId, double Score);
}
