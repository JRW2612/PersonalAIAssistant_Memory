using PersonalAIAssistant.Memory.Core.Domains.Enums;

namespace PersonalAIAssistant.Memory.Core.Entities
{
    public class MemoryReadModelEntity
    {
        public Guid MemoryId { get; set; }
        public string StreamId { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public int TokenCount { get; set; }
        public MemoryImportance Importance { get; set; } = MemoryImportance.Medium;
        public DateTime CreatedAt { get; set; }
        public bool Archived { get; set; }
        public DateTime? LastProcessedAt { get; set; }
    }
}

