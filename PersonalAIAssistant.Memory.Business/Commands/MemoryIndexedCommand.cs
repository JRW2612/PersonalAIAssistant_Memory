using MediatR;

namespace PersonalAIAssistant.Memory.Business.Commands
{
    public record MemoryIndexedCommand
    (
         Guid MemoryId,
         string EmbeddingId,
         string VectorProvider  // e.g. "Pinecone", "FAISS"
    ) : IRequest<bool>;
}
