using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public record UpdateMemoryCommand
    (
         Guid MemoryId,
         IReadOnlyDictionary<string, string>? UpdatedFields = null
    ) : IRequest<Guid>;
}
