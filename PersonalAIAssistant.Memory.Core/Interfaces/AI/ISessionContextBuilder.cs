// PersonalAIAssistant.Memory.Core/Interfaces/AI/ISessionContextBuilder.cs
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Core.Interfaces.AI;

/// <summary>
/// Assembles a complete session context for the active user message by fusing:
/// <list type="bullet">
///   <item>Short-term: recent conversation turns from the buffer</item>
///   <item>Long-term: semantically relevant memories retrieved from the vector store</item>
/// </list>
/// The resulting <see cref="SessionContext"/> contains a ready-to-use
/// <c>SystemPromptFragment</c> that callers inject into the LLM system prompt.
/// </summary>
public interface ISessionContextBuilder
{
    /// <summary>
    /// Builds the session context for the current user message.
    /// If the conversation buffer has reached the flush threshold this call will
    /// also promote the oldest turns to long-term memory (fire-and-forget style; errors are logged but not thrown).
    /// </summary>
    Task<SessionContext> BuildAsync(SessionContextRequest request, CancellationToken ct = default);
}
