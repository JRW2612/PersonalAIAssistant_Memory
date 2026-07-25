using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public record UpdateMemoryCommand
    (
         Guid MemoryId,
         string UserId,
         IReadOnlyDictionary<string, string>? UpdatedFields = null
    ) : IRequest<Guid>;
}
