using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class CompressMemoryCommand : IRequest<Guid>
    {
        public Guid OriginalMemoryId { get; set; }
        public string CompressedText { get; set; }
        public string CompressionModel { get; set; }   // e.g. "GPT-4 summary"
        public int TokenCount { get; set; }
    }
}
