using PersonalAIAssistant.Memory.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    public interface IMemoryRetrievalService
    {
        Task<IReadOnlyList<RetrievedMemory>> RetrieveAsync(
            RetrievalRequest request, CancellationToken ct);
    }
}
