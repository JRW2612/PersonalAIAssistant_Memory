// PersonalAIAssistant.Memory.Core/Models/ConversationTurn.cs
namespace PersonalAIAssistant.Memory.Core.Models;

/// <summary>
/// A single message turn in a conversation session.
/// <para>
/// <c>Role</c> follows the OpenAI convention: <c>"user"</c> or <c>"assistant"</c>.
/// </para>
/// </summary>
public record ConversationTurn(
    string Role,
    string Content,
    DateTime Timestamp);
