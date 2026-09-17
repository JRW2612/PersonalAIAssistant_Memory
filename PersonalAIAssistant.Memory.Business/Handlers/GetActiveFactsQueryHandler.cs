using MediatR;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Handlers;

public class GetActiveFactsQueryHandler : IRequestHandler<GetActiveFactsQuery, IReadOnlyList<TemporalFact>>
{
    private readonly ITemporalFactRepository _factRepo;

    public GetActiveFactsQueryHandler(ITemporalFactRepository factRepo)
    {
        _factRepo = factRepo;
    }

    public async Task<IReadOnlyList<TemporalFact>> Handle(GetActiveFactsQuery request, CancellationToken cancellationToken)
    {
        return await _factRepo.GetCurrentFactsAsync(request.UserId, request.Subject, cancellationToken);
    }
}
