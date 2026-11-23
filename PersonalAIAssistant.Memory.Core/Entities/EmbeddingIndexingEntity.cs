namespace PersonalAIAssistant.Memory.Core.Entities;

public class EmbeddingIndexingEntity
{
    public Guid AggregateId { get; set; }
    public int EventVersion { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Failure { get; set; }
}
