using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class DeleteMemoryCommand
   (
         Guid MemoryId,
         string Reason  // e.g. "user request", "TTL expired"
    ) : IRequest<bool>;
}
