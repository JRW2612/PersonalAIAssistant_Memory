// PersonalAIAssistant.Memory.Business/Commands/AppendConversationTurnCommand.cs
using MediatR;
using PersonalAIAssistant.Memory.Core.Interfaces.Common;

namespace PersonalAIAssistant.Memory.Business.Commands;

/// <summary>
/// Appends a new conversation turn into the user's active session buffer.
/// </summary>
public record AppendConversationTurnCommand(
    string UserId,
    string SessionId,
    string Role,
    string Content,
    string? CorrelationId = null
) : IRequest<bool>, ICorrelatedRequest;
