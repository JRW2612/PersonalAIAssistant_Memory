// PersonalAIAssistant.Memory.Business/Handlers/BuildSessionContextQueryHandler.cs
using MediatR;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Handlers;

public class BuildSessionContextQueryHandler : IRequestHandler<BuildSessionContextQuery, SessionContext>
{
    private readonly ISessionContextBuilder _contextBuilder;

    public BuildSessionContextQueryHandler(ISessionContextBuilder contextBuilder)
    {
        _contextBuilder = contextBuilder;
    }

    public async Task<SessionContext> Handle(BuildSessionContextQuery request, CancellationToken cancellationToken)
    {
        var sessionRequest = new SessionContextRequest(
            UserId: request.UserId,
            SessionId: request.SessionId,
            CurrentMessage: request.CurrentMessage,
            TopK: request.TopK,
            MaxBufferTurns: request.MaxBufferTurns);

        return await _contextBuilder.BuildAsync(sessionRequest, cancellationToken);
    }
}
