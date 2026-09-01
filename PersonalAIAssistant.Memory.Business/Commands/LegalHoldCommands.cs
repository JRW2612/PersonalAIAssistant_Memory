using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public record ApplyLegalHoldCommand(Guid MemoryId, string Reason, string AuditorId) : IRequest;
    public record ReleaseLegalHoldCommand(Guid MemoryId, string AuditorId) : IRequest;
}
