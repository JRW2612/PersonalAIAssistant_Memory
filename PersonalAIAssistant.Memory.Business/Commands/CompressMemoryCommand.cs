using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public record CompressMemoryCommand
    (
         Guid OriginalMemoryId,
         string CompressedText,
         string CompressionModel,   // e.g. "GPT-4 summary"
         int TokenCount,
         string UserId
    ) : IRequest<Guid>;
}
