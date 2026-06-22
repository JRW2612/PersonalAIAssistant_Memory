namespace PersonalAIAssistant.Memory.Events
{
    public abstract class MemoryEvent
    {
        public Guid EventId { get; set; } = Guid.NewGuid();
        public Guid AggregateId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int Version { get; set; }
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public string? UserId { get; set; }
    }
}
