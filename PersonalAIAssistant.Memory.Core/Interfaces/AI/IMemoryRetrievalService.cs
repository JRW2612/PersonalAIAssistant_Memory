using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Core.Interfaces.AI
{
    public interface IMemoryRetrievalService
    {
        Task<IReadOnlyList<RetrievedMemory>> RetrieveAsync(RetrievalRequest request, CancellationToken ct);
    }
}
