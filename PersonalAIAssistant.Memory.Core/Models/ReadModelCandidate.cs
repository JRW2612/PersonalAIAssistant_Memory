namespace PersonalAIAssistant.Memory.Core.Models
{
    /// <summary>
    /// Represents a memory entry selected for consolidation/compression.
    /// </summary>
    public record ReadModelCandidate
    (
        Guid MemoryId,          // Unique identifier of the memory
        string StreamId,        // Event stream id (e.g. "memory-{MemoryId}")
        string Text,            // Raw or summarized text to be compressed
        int TokenCount,         // Current token count (helps decide if compression is needed)
        DateTime CreatedAt,     // When the memory was originally created
        bool IsArchived = false // Flag to skip archived/deleted memories
    );
}
