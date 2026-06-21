namespace PersonalAIAssistant.Memory.Events
{
    public class MemoryAddedEvent : MemoryEvent
    {
        public string RawText { get; set; }
        public string Source { get; set; }   // e.g. "chat", "email", "note"
        public List<string> Tags { get; set; } = new();
    }
}
