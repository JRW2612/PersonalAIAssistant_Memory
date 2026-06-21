namespace PersonalAIAssistant.Memory.Events
{
    public abstract class MemoryEvent
    {
        public Guid EventId { get; set; } = Guid.NewGuid();
        public Guid AggregateId { get; set; }
        public string EventType { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int Version { get; set; }
    }
}
