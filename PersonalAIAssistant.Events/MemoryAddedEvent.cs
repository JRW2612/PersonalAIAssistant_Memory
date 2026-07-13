namespace PersonalAIAssistant.Memory.Events
{
    public class MemoryAddedEvent : MemoryEvent
    {
        public string RawText { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;   // e.g. "chat", "email", "note"
        public List<string> Tags { get; set; } = new();
    }
}
