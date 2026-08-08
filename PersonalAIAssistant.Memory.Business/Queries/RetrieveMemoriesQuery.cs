using MediatR;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Queries
{
    public record RetrieveMemoriesQuery(
        string UserId,
        string QueryText,
        int TopK = 5,
        string? ProviderOverride = null) : IRequest<FusedMemoryPrompt>;
}
