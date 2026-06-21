namespace PersonalAIAssistant.Memory.Events
{
    public class MemoryUpdatedEvent : MemoryEvent
    {
        public Guid MemoryId { get; set; }
        public Dictionary<string, string> UpdatedFields { get; set; } = new();
    }
}
