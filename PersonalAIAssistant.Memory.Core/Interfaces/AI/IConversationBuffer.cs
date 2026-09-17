// PersonalAIAssistant.Memory.Core/Interfaces/AI/IConversationBuffer.cs
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Core.Interfaces.AI;

/// <summary>
/// Short-term, ephemeral store for the recent turns of a user's conversation session.
/// Turns accumulate here before being compressed and promoted to long-term memory.
/// Implementations must be safe to call from multiple concurrent requests.
/// </summary>
public interface IConversationBuffer
{
    /// <summary>
    /// Appends a single conversation turn (user or assistant message) to the buffer for the
    /// given <paramref name="userId"/> / <paramref name="sessionId"/> pair.
    /// </summary>
    Task AppendTurnAsync(
        string userId,
        string sessionId,
        string role,
        string content,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the <paramref name="maxTurns"/> most-recent turns in chronological order
    /// (oldest first), without removing them from the buffer.
    /// </summary>
    Task<IReadOnlyList<ConversationTurn>> GetRecentTurnsAsync(
        string userId,
        string sessionId,
        int maxTurns,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the current total number of turns in the buffer for the session.
    /// </summary>
    Task<int> GetTurnCountAsync(
        string userId,
        string sessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Reads all turns from the buffer and atomically clears it, returning the evicted turns
    /// so the caller can compress and promote them to long-term memory.
    /// </summary>
    Task<IReadOnlyList<ConversationTurn>> FlushAndClearAsync(
        string userId,
        string sessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Explicitly clears the buffer without returning the turns (e.g. on logout or session end).
    /// </summary>
    Task ClearAsync(
        string userId,
        string sessionId,
        CancellationToken ct = default);
}
