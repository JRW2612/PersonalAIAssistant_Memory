namespace PersonalAIAssistant.Memory.Events
{
    public class MemoryCompressedEvent : MemoryEvent
    {
        public Guid OriginalMemoryId { get; set; }
        public string CompressedText { get; set; } = string.Empty;
        public string CompressionModel { get; set; } = string.Empty;   // e.g. "GPT-4 summary"
        public int TokenCount { get; set; }
    }
}
