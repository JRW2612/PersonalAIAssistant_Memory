using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public record ConsolidateMemoriesCommand
    (
         Guid NewMemoryId,
         IReadOnlyList<Guid> MergedMemoryIds,
         string ConsolidatedText,
         IReadOnlyList<string> ProvenanceLinks
    ) : IRequest<Guid>;
}
