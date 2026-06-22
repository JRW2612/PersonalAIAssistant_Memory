namespace PersonalAIAssistant.Memory.Core.Entities
{
    public class ProcessingLockEntity
    {
        public Guid MemoryId { get; set; }
        public DateTime LockedAt { get; set; }
    }
}
