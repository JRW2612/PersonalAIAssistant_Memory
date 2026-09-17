namespace PersonalAIAssistant.Memory.Core.Entities;

/// <summary>
/// EF Core persistence model for bi-temporal facts.
/// </summary>
public class TemporalFactEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Predicate { get; set; } = string.Empty;
    public string Object { get; set; } = string.Empty;
    public DateTime ValidFromUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ValidToUtc { get; set; }
    public bool IsCurrent { get; set; } = true;
    public double Confidence { get; set; } = 1.0;
    public string? Source { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? InvalidatedAtUtc { get; set; }
}
