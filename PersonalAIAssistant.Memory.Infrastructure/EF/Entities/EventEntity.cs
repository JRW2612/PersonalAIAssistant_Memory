namespace PersonalAIAssistant.Memory.Infrastructure.EF.Entities
{
    public class EventEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string StreamId { get; set; } = string.Empty;
        public Guid EventId { get; set; }
        public int Version { get; set; }
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public string AggregateId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public bool IsEncrypted { get; set; }
    }
}
