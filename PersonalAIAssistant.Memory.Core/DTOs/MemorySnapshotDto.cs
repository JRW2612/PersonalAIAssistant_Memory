namespace PersonalAIAssistant.Memory.Core.DTOs
{
    public record MemorySnapshotDto
    {
        public Guid Id { get; init; }
        public int Version { get; init; }
        public string RawText { get; init; } = string.Empty;
        public string? CompressedText { get; init; }
        public string? ConsolidatedText { get; init; }
        public string? EmbeddingId { get; init; }
        public List<string> Tags { get; init; } = new();
    }
}
