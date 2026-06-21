using PersonalAIAssistant.Memory.Core.Domains.Enums;

namespace PersonalAIAssistant.Memory.Core.Models
{
    public class MemoryReadModel
    {
        public Guid MemoryId { get; set; }
        public string Summary { get; set; }
        public int TokenCount { get; set; }
        public bool Archived { get; set; }
        public MemoryImportance Importance { get; set; } // enum
    }
}
