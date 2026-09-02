namespace PersonalAIAssistant.Memory.Core.Messages
{
    public class OutboxMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public string MessageType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty; // JSON payload
        public IDictionary<string, string>? Headers { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}
