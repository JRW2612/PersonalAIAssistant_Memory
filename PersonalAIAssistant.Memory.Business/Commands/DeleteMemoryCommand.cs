using MediatR;
using PersonalAIAssistant.Memory.Business.Security;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public record DeleteMemoryCommand
    (
        Guid MemoryId,
        string Reason, // e.g. "user request", "TTL expired"
        string UserId,
        string TenantId = "default"
    ) : IRequest<Unit>, IAuthorizedRequest;
}
