using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Persistence;

/// <summary>
/// Persistence store for queryable temporal facts and entity relationships.
/// </summary>
public interface ITemporalFactRepository
{
    /// <summary>
    /// Upserts a temporal fact into the read store.
    /// </summary>
    Task UpsertFactAsync(TemporalFact fact, string userId, CancellationToken ct = default);

    /// <summary>
    /// Soft-invalidates any active fact matching Subject and Predicate, setting ValidTo and IsCurrent=false.
    /// </summary>
    Task InvalidateFactAsync(string userId, string subject, string predicate, DateTime invalidatedAtUtc, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all currently valid facts for a user, optionally filtered by subject.
    /// </summary>
    Task<IReadOnlyList<TemporalFact>> GetCurrentFactsAsync(string userId, string? subject = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves historical evolution of a specific fact over time (both active and expired).
    /// </summary>
    Task<IReadOnlyList<TemporalFact>> GetFactHistoryAsync(string userId, string subject, string predicate, CancellationToken ct = default);
}
