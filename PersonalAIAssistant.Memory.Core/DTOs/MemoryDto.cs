namespace PersonalAIAssistant.Memory.Core.DTOs
{
    public class MemoryDto
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
