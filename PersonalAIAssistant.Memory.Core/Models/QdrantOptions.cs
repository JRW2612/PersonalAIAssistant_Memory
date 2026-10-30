namespace PersonalAIAssistant.Memory.Core.Models
{
    public enum QdrantMode
    {
        LocalEdge,
        Cloud
    }

    public enum LocalEdgeProvider
    {
        Auto,
        EmbeddedInProcess,
        LocalQdrantContainer
    }

    public sealed class QdrantOptions
    {
        public const string SectionName = "Qdrant";

        /// <summary>
        /// Deployment mode: "LocalEdge" for offline/on-device developer execution, "Cloud" for managed Qdrant Cloud.
        /// </summary>
        public string Mode { get; set; } = "LocalEdge";

        /// <summary>
        /// Local edge provider strategy when Mode is LocalEdge:
        /// "Auto" (attempts local container, falls back to embedded in-process store),
        /// "EmbeddedInProcess" (pure standalone embedded file-backed vector database),
        /// or "LocalQdrantContainer" (expects local Docker/Qdrant container).
        /// </summary>
        public string LocalEdgeProvider { get; set; } = "Auto";

        /// <summary>
        /// Path on disk for embedded local edge vector persistence when running offline.
        /// </summary>
        public string LocalStoragePath { get; set; } = "./data/edge_vectors";

        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 6334;
        public string CollectionName { get; set; } = "memories";
        public bool Https { get; set; } = false;
        public string ApiKey { get; set; } = string.Empty;

        public bool IsCloudMode => string.Equals(Mode, "Cloud", StringComparison.OrdinalIgnoreCase);
        public bool IsLocalEdgeMode => !IsCloudMode;
    }
}
