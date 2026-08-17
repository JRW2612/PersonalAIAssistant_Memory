using System;

namespace PersonalAIAssistant.Memory.Api.DTOs
{
    /// <summary>
    /// Response payload returned after triggering memory compression.
    /// </summary>
    /// <param name="MemoryId">The unique identifier of the compressed memory.</param>
    /// <param name="Status">The resulting operational status.</param>
    public record CompressMemoryResponseDto(
        Guid MemoryId,
        string Status
    );
}
