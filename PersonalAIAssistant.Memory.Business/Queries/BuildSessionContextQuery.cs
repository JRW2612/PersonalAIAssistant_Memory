// PersonalAIAssistant.Memory.Business/Queries/BuildSessionContextQuery.cs
using MediatR;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Queries;

/// <summary>
/// Query to assemble fused short-term conversation context and long-term memories
/// for the current turn.
/// </summary>
public record BuildSessionContextQuery(
    string UserId,
    string SessionId,
    string CurrentMessage,
    int TopK = 5,
    int MaxBufferTurns = 10
) : IRequest<SessionContext>;
