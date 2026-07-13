namespace PersonalAIAssistant.Memory.Events
{
    public class SnapshotCreatedEvent : MemoryEvent
    {
        public Guid AggregateIdSnapshot { get; set; }
        public string SnapshotPayload { get; set; } = string.Empty;   // JSON summary of state
        public int SnapshotVersion { get; set; }
    }
}
