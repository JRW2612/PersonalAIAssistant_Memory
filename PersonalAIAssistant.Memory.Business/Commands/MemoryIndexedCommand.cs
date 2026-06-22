using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public class MemoryIndexedCommand
    (
         Guid MemoryId,
         string EmbeddingId,
         string VectorProvider  // e.g. "Pinecone", "FAISS"
    ) : IRequest<bool>;
}
