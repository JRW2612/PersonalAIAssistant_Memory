using MediatR;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Handlers;

public class GetTemporalFactHistoryQueryHandler : IRequestHandler<GetTemporalFactHistoryQuery, IReadOnlyList<TemporalFact>>
{
    private readonly ITemporalFactRepository _factRepository;

    public GetTemporalFactHistoryQueryHandler(ITemporalFactRepository factRepository)
    {
        _factRepository = factRepository;
    }

    public Task<IReadOnlyList<TemporalFact>> Handle(GetTemporalFactHistoryQuery request, CancellationToken cancellationToken)
    {
        return _factRepository.GetFactHistoryAsync(
            request.UserId,
            request.Subject,
            request.Predicate,
            cancellationToken);
    }
}
