namespace PersonalAIAssistant.Memory.Events
{
    public class MemoryIndexedEvent : MemoryEvent
    {
        public Guid MemoryId { get; set; }
        public string EmbeddingId { get; set; }
        public string VectorProvider { get; set; }   // e.g. "Pinecone", "FAISS"
    }
}
