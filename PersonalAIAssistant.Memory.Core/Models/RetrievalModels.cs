using PersonalAIAssistant.Memory.Core.Domains.Enums;
using System;
using System.Collections.Generic;

namespace PersonalAIAssistant.Memory.Core.Models
{
    public record RetrievalRequest(
        string UserId,
        string QueryText,
        int TopK = 5,
        string? PreferredProvider = null);

    public record RetrievedMemory(
        Guid MemoryId,
        string Text,
        double Score,
        double VectorScore,
        double RecencyScore,
        MemoryImportance Importance,
        DateTime CreatedAt);

    public record FusedMemoryPrompt(
        string SystemContext,
        IReadOnlyList<RetrievedMemory> Sources);
}
