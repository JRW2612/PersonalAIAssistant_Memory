using MediatR;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.Common;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public record AddMemoryCommand
    (
        string RawText,
        string Source,
        IReadOnlyList<string> Tags,
        string UserId,
        MemoryImportance Importance = MemoryImportance.Medium,
        string? CorrelationId = null
    ) : IRequest<Guid>, ICorrelatedRequest;
}
