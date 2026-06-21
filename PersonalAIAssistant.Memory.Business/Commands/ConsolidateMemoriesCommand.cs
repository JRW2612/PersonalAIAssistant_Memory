using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class ConsolidateMemoriesCommand : IRequest<Guid>
    {
        public Guid NewMemoryId { get; set; }
        public List<Guid> MergedMemoryIds { get; set; } = new();
        public string ConsolidatedText { get; set; }
        public List<string> ProvenanceLinks { get; set; } = new();
    }
}
