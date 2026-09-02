namespace PersonalAIAssistant.Memory.Api.DTOs
{
    /// <summary>
    /// Response payload returned after successfully creating/ingesting a memory.
    /// </summary>
    /// <param name="MemoryId">The unique identifier of the created memory aggregate.</param>
    public record CreateMemoryResponseDto(
        Guid MemoryId
    );
}
