using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class MemoryIndexedCommand : IRequest<bool>
    {
        public Guid MemoryId { get; set; }
        public string EmbeddingId { get; set; }
        public string VectorProvider { get; set; }   // e.g. "Pinecone", "FAISS"
    }
}
