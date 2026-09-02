using MediatR;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;
using System.Text;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    public class RetrieveMemoriesQueryHandler : IRequestHandler<RetrieveMemoriesQuery, FusedMemoryPrompt>
    {
        private readonly IMemoryRetrievalService _retrievalService;

        public RetrieveMemoriesQueryHandler(IMemoryRetrievalService retrievalService)
        {
            _retrievalService = retrievalService;
        }

        public async Task<FusedMemoryPrompt> Handle(RetrieveMemoriesQuery request, CancellationToken cancellationToken)
        {
            var retrievalRequest = new RetrievalRequest(
                request.UserId,
                request.QueryText,
                request.TopK,
                request.ProviderOverride);

            var memories = await _retrievalService.RetrieveAsync(retrievalRequest, cancellationToken);

            var sb = new StringBuilder();
            sb.AppendLine("Below are relevant memories retrieved from the user's past interactions:");
            for (int i = 0; i < memories.Count; i++)
            {
                var m = memories[i];
                sb.AppendLine($"[{i + 1}] (Relevance: {m.Score:F2}, Importance: {m.Importance}, Date: {m.CreatedAt:yyyy-MM-dd})");
                sb.AppendLine(m.Text);
                sb.AppendLine();
            }

            return new FusedMemoryPrompt(sb.ToString().TrimEnd(), memories);
        }
    }
}
