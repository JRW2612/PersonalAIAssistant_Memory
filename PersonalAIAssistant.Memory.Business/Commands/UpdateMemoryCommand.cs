using MediatR;
using PersonalAIAssistant.Memory.Business.Security;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public record UpdateMemoryCommand
    (
         Guid MemoryId,
         string UserId,
         IReadOnlyDictionary<string, string>? UpdatedFields = null
    ) : IRequest<Guid>, IAuthorizedRequest;
}
