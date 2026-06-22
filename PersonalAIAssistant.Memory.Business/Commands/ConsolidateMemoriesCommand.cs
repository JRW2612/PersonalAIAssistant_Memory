using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class ConsolidateMemoriesCommand
    (
         Guid NewMemoryId,
         List<Guid> MergedMemoryIds,
         string ConsolidatedText,
         List<string> ProvenanceLinks
    ) : IRequest<Guid>;
}
