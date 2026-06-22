namespace PersonalAIAssistant.Memory.Core.Entities
{
    public class ProcessedEventEntity
    {
        public int Id { get; set; }
        public Guid AggregateId { get; set; }
        public int Version { get; set; }
        public DateTime ProcessedAt { get; set; }
    }
}
