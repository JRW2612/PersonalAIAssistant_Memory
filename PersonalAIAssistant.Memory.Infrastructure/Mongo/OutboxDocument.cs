namespace PersonalAIAssistant.Memory.Infrastructure.Mongo
{
    public class OutboxDocument
    {
        // Simple Mongo document that mirrors the outbox message stored with events.
        // The dispatcher reads these and publishes them to the message broker.
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public Guid MessageId { get; set; }
        public string MessageType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; }
        public DateTime? DispatchedAt { get; set; }
        public int Attempts { get; set; }

        public static OutboxDocument FromOutboxMessage(PersonalAIAssistant.Memory.Core.Messages.OutboxMessage m)
        {
            return new OutboxDocument
            {
                MessageId = m.MessageId,
                MessageType = m.MessageType,
                Payload = m.Payload,
                OccurredAt = m.OccurredAt,
                Attempts = 0
            };
        }
    }
}
