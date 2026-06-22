using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class UpdateMemoryCommand
    (
         Guid MemoryId,
         Dictionary<string, string> UpdatedFields = null
    ) : IRequest<Guid>;
}
