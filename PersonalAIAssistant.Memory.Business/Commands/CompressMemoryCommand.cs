using MediatR;
using PersonalAIAssistant.Memory.Business.Security;

namespace PersonalAIAssistant.Memory.Business.Commands;

public record CompressMemoryCommand
(
     Guid OriginalMemoryId,
     string CompressedText,
     string CompressionModel,   // e.g. "GPT-4 summary"
     int TokenCount,
     string UserId,
     string TenantId = "default"
) : IRequest<Guid>, IAuthorizedRequest
{
    public Guid MemoryId => OriginalMemoryId;
}
