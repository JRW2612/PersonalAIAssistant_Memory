using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public record ConsolidateMemoriesCommand
    (
         Guid NewMemoryId,
         IReadOnlyList<Guid> MergedMemoryIds,
         string ConsolidatedText,
         string UserId,
         IReadOnlyList<string> ProvenanceLinks
    ) : IRequest<Guid>;
}
