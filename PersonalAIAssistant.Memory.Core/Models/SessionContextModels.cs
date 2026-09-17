// PersonalAIAssistant.Memory.Core/Models/SessionContextModels.cs
namespace PersonalAIAssistant.Memory.Core.Models;

/// <summary>
/// Input to <c>ISessionContextBuilder.BuildAsync</c>.
/// </summary>
/// <param name="UserId">The authenticated user's identity.</param>
/// <param name="SessionId">Unique identifier for this conversation session. Allows a single user to have multiple concurrent sessions (mobile, web, etc.).</param>
/// <param name="CurrentMessage">The user's current (un-sent) message text, used as the query for semantic memory retrieval.</param>
/// <param name="TopK">Number of long-term memories to retrieve. Default: 5.</param>
/// <param name="MaxBufferTurns">Maximum number of recent turns to include in context. Default: 10.</param>
public record SessionContextRequest(
    string UserId,
    string SessionId,
    string CurrentMessage,
    int TopK = 5,
    int MaxBufferTurns = 10);

/// <summary>
/// The assembled context returned by <c>ISessionContextBuilder.BuildAsync</c>.
/// </summary>
/// <param name="SystemPromptFragment">
/// A fully-formatted string ready to be injected into the LLM system prompt.
/// Includes a "Recent Conversation" section and a "Relevant Long-Term Memories" section.
/// </param>
/// <param name="RecentTurns">The raw short-term turns that were included.</param>
/// <param name="LongTermMemories">The retrieved long-term memory records (for metadata / source attribution).</param>
public record SessionContext(
    string SystemPromptFragment,
    IReadOnlyList<ConversationTurn> RecentTurns,
    IReadOnlyList<RetrievedMemory> LongTermMemories);

/// <summary>
/// Configuration options for the conversation buffer.
/// Bind from the <c>"ConversationBuffer"</c> section in appsettings.
/// </summary>
public sealed class ConversationBufferOptions
{
    public const string SectionName = "ConversationBuffer";

    /// <summary>
    /// Maximum number of turns kept per session in the buffer.
    /// When exceeded, the oldest turns beyond this cap are silently dropped.
    /// Default: 20.
    /// </summary>
    public int MaxTurns { get; set; } = 20;

    /// <summary>
    /// When the buffer reaches this number of turns, the oldest batch is compressed and
    /// promoted to long-term memory automatically.
    /// Must be less than or equal to <see cref="MaxTurns"/>.
    /// Default: 10.
    /// </summary>
    public int FlushThreshold { get; set; } = 10;

    /// <summary>
    /// Sliding TTL for a session's buffer in Redis.
    /// A session that has been idle longer than this will be automatically evicted.
    /// Default: 60 minutes.
    /// </summary>
    public int SessionTtlMinutes { get; set; } = 60;

    /// <summary>
    /// When true, reaching the <see cref="FlushThreshold"/> automatically promotes
    /// compressed turns to long-term memory via <c>AddMemoryCommand</c>.
    /// Default: true.
    /// </summary>
    public bool AutoFlushToMemory { get; set; } = true;
}
