namespace PersonalAIAssistant.Memory.Api.DTOs
{
    /// <summary>
    /// Request payload for explicitly triggering LLM compression on a specific memory.
    /// </summary>
    /// <param name="CompressedText">The compressed/summarized representation of the memory.</param>
    /// <param name="Model">The AI model utilized to perform the summarization (e.g. gpt-4o-mini).</param>
    /// <param name="TokenCount">Estimated or counted token usage of the compressed summary.</param>
    public record CompressRequestDto(
        string CompressedText,
        string? Model = null,
        int TokenCount = 0
    );
}
