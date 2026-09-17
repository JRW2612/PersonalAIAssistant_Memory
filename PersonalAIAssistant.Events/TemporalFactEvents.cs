namespace PersonalAIAssistant.Memory.Events;

/// <summary>
/// Emitted when an entity fact (Subject-Predicate-Object) is asserted as true starting from ValidFromUtc.
/// </summary>
public class TemporalFactAssertedEvent : MemoryEvent
{
    public Guid FactId { get; set; } = Guid.NewGuid();
    public string Subject { get; set; } = string.Empty;
    public string Predicate { get; set; } = string.Empty;
    public string Object { get; set; } = string.Empty;
    public DateTime ValidFromUtc { get; set; } = DateTime.UtcNow;
    public double Confidence { get; set; } = 1.0;
    public string? Source { get; set; }

    public TemporalFactAssertedEvent()
    {
        EventType = nameof(TemporalFactAssertedEvent);
    }
}

/// <summary>
/// Emitted when a prior fact is invalidated/superseded because a contradictory or updated assertion occurred.
/// </summary>
public class TemporalFactInvalidatedEvent : MemoryEvent
{
    public Guid FactId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Predicate { get; set; } = string.Empty;
    public string OldObject { get; set; } = string.Empty;
    public DateTime InvalidatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }

    public TemporalFactInvalidatedEvent()
    {
        EventType = nameof(TemporalFactInvalidatedEvent);
    }
}
