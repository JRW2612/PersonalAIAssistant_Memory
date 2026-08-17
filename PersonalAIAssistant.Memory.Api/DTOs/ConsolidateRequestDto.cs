using System;
using System.Collections.Generic;

namespace PersonalAIAssistant.Memory.Api.DTOs
{
    /// <summary>
    /// Request payload for explicitly triggering consolidation across multiple memory candidates.
    /// </summary>
    /// <param name="MergedMemoryIds">List of memory IDs being merged into a single consolidated memory.</param>
    /// <param name="ConsolidatedText">The resulting unified knowledge / memory narrative.</param>
    /// <param name="ProvenanceLinks">Provenance source links or IDs traceably referencing original inputs.</param>
    public record ConsolidateRequestDto(
        List<Guid> MergedMemoryIds,
        string ConsolidatedText,
        List<string>? ProvenanceLinks = null
    );
}
