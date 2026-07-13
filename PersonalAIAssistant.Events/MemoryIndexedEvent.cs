namespace PersonalAIAssistant.Memory.Events
{
    public class MemoryIndexedEvent : MemoryEvent
    {
        public Guid MemoryId { get; set; }
        public string EmbeddingId { get; set; } = string.Empty;
        public string VectorProvider { get; set; } = string.Empty;   // e.g. "Pinecone", "FAISS"
    }
}
