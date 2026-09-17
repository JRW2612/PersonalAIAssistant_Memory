// PersonalAIAssistant.Memory.Api/DTOs/SessionDtos.cs
namespace PersonalAIAssistant.Memory.Api.DTOs;

/// <summary>
/// Request payload to append a conversation turn to a session buffer.
/// </summary>
/// <param name="Role">Sender role: 'user', 'assistant', or 'system'.</param>
/// <param name="Content">Turn message content.</param>
public record AppendTurnDto(
    string Role,
    string Content);

/// <summary>
/// Request payload to assemble a fused prompt context for the active session.
/// </summary>
/// <param name="CurrentMessage">The user's latest prompt message to query long-term memory.</param>
/// <param name="TopK">Maximum number of relevant long-term memories to retrieve.</param>
/// <param name="MaxBufferTurns">Maximum recent short-term turns from the buffer to include.</param>
public record GetSessionContextDto(
    string CurrentMessage,
    int TopK = 5,
    int MaxBufferTurns = 10);
