namespace PersonalAIAssistant.Memory.Api.DTOs
{
    /// <summary>
    /// Response payload returned after triggering memory consolidation.
    /// </summary>
    /// <param name="NewMemoryId">The unique identifier of the newly consolidated memory aggregate.</param>
    /// <param name="Status">The resulting operational status.</param>
    public record ConsolidateMemoriesResponseDto(
        Guid NewMemoryId,
        string Status
    );
}
