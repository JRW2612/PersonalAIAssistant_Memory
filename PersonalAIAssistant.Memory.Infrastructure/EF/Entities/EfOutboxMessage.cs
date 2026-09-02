namespace PersonalAIAssistant.Memory.Infrastructure.EF.Entities
{
    public class EfOutboxMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid MessageId { get; set; }
        public string MessageType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public DateTime? DispatchedAt { get; set; }
        public int Attempts { get; set; }
    }
}
