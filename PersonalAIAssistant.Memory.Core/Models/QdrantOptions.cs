namespace PersonalAIAssistant.Memory.Core.Models
{
    public sealed class QdrantOptions
    {
        public const string SectionName = "Qdrant";
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 6334;
        public string CollectionName { get; set; } = "memories";
        public bool Https { get; set; } = false;
        public string ApiKey { get; set; } = string.Empty;
    }
}
