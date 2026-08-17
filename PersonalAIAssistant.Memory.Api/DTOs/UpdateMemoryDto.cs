namespace PersonalAIAssistant.Memory.Api.DTOs
{
    /// <summary>
    /// Request payload for updating the text content of an existing memory aggregate.
    /// </summary>
    /// <param name="RawText">The updated raw text payload.</param>
    public record UpdateMemoryDto(
        string RawText
    );
}
