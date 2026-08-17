using PersonalAIAssistant.Memory.Core.Domains.Enums;
using System.Collections.Generic;

namespace PersonalAIAssistant.Memory.Api.DTOs
{
    /// <summary>
    /// Request payload for creating and ingesting a new memory.
    /// </summary>
    /// <param name="RawText">The raw text content of the memory.</param>
    /// <param name="Source">The originating source of the memory (e.g., Api, User, Slack, Teams).</param>
    /// <param name="Importance">The semantic importance tier of the memory.</param>
    /// <param name="Tags">Optional list of contextual tags.</param>
    /// <param name="CorrelationId">Optional correlation ID for distributed tracing.</param>
    public record CreateMemoryDto(
        string RawText,
        string? Source = null,
        MemoryImportance Importance = MemoryImportance.Medium,
        List<string>? Tags = null,
        string? CorrelationId = null
    );
}
