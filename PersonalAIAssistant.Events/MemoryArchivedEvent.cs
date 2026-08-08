using System;

namespace PersonalAIAssistant.Memory.Events
{
    public class MemoryArchivedEvent : MemoryEvent
    {
        public Guid MemoryId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
