namespace PersonalAIAssistant.Memory.Events
{
    public class MemoryDeletedEvent : MemoryEvent
    {
        public Guid MemoryId { get; set; }
        public string Reason { get; set; } = string.Empty;   // e.g. "user request", "TTL expired"
    }
}
