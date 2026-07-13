namespace PersonalAIAssistant.Memory.Events
{
    public class MemoryConsolidatedEvent : MemoryEvent
    {
        public Guid NewMemoryId { get; set; }
        public List<Guid> MergedMemoryIds { get; set; } = new();
        public string ConsolidatedText { get; set; } = string.Empty;
        public List<string> ProvenanceLinks { get; set; } = new();
    }
}
