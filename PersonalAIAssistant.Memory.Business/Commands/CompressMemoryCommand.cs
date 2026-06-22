using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class CompressMemoryCommand
    (
         Guid OriginalMemoryId,
         string CompressedText,
         string CompressionModel,   // e.g. "GPT-4 summary"
         int TokenCount
    ) : IRequest<Guid>;
}
