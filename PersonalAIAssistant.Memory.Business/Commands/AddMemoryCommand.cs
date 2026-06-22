using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public record AddMemoryCommand
    (
        string RawText,
        string Source,
        IReadOnlyList<string> Tags,
        string UserId,
        string? CorrelationId = null
    ) : IRequest<Guid>;
}

