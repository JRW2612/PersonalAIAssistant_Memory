namespace PersonalAIAssistant.Memory.Events
{
    public class MemoryCompressedEvent : MemoryEvent
    {
        public Guid OriginalMemoryId { get; set; }
        public string CompressedText { get; set; }
        public string CompressionModel { get; set; }   // e.g. "GPT-4 summary"
        public int TokenCount { get; set; }
    }
}
