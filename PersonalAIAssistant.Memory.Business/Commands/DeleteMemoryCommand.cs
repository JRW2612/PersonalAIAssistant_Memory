using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public record DeleteMemoryCommand
   (
        Guid MemoryId,
        string Reason, // e.g. "user request", "TTL expired"
        string UserId
    ) : IRequest<Unit>;
}
