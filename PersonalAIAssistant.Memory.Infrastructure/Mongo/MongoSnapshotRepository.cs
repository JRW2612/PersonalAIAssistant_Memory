using MongoDB.Driver;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using System.Text.Json;

namespace PersonalAIAssistant.Memory.Infrastructure.Mongo
{
    /// <summary>
    /// MongoDB-backed implementation of <see cref="ISnapshotRepository"/>.
    /// Snapshots are stored as JSON documents in a dedicated "snapshots" collection.
    /// Each save creates a new document (append-only); the latest is always the one with the
    /// highest <c>Version</c> for a given <c>StreamId</c>.
    /// </summary>
    public class MongoSnapshotRepository : ISnapshotRepository
    {
        private readonly IMongoCollection<SnapshotDocument> _collection;
        private readonly IEventStore _eventStore;

        public MongoSnapshotRepository(
            IMongoDatabase database,
            IEventStore eventStore,
            string collectionName = "snapshots")
        {
            _collection = database.GetCollection<SnapshotDocument>(collectionName);
            _eventStore = eventStore;

            // Composite index: StreamId + Version for efficient latest-snapshot queries.
            try
            {
                var indexKeys = Builders<SnapshotDocument>.IndexKeys
                    .Ascending(d => d.StreamId)
                    .Descending(d => d.Version);
                _collection.Indexes.CreateOne(new CreateIndexModel<SnapshotDocument>(indexKeys));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MongoSnapshotRepository] Warning: Could not create index on startup: {ex.Message}");
            }
        }

        public async Task<IReadOnlyList<string>> GetStreamsNeedingSnapshotAsync(
            int eventThreshold, int limit, CancellationToken ct)
        {
            var streamSummaries = await _eventStore.GetStreamSummariesAsync(limit * 3, ct);

            var result = new List<string>();
            foreach (var (streamId, currentVersion) in streamSummaries)
            {
                if (result.Count >= limit) break;

                var latestSnapshot = await _collection
                    .Find(Builders<SnapshotDocument>.Filter.Eq(d => d.StreamId, streamId))
                    .Sort(Builders<SnapshotDocument>.Sort.Descending(d => d.Version))
                    .Limit(1)
                    .FirstOrDefaultAsync(ct);

                var snapshotVersion = latestSnapshot?.Version ?? 0;
                var eventsSinceSnapshot = currentVersion - snapshotVersion;

                if (eventsSinceSnapshot >= eventThreshold)
                    result.Add(streamId);
            }

            return result;
        }

        public async Task<MemorySnapshotDto?> GetLatestSnapshotAsync(string streamId, CancellationToken ct)
        {
            var doc = await _collection
                .Find(Builders<SnapshotDocument>.Filter.Eq(d => d.StreamId, streamId))
                .Sort(Builders<SnapshotDocument>.Sort.Descending(d => d.Version))
                .Limit(1)
                .FirstOrDefaultAsync(ct);

            if (doc is null) return null;

            return JsonSerializer.Deserialize<MemorySnapshotDto>(doc.Payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public async Task SaveSnapshotAsync(string streamId, string payload, int version, CancellationToken ct)
        {
            var doc = new SnapshotDocument
            {
                StreamId = streamId,
                Payload = payload,
                Version = version,
                CreatedAt = DateTime.UtcNow
            };
            await _collection.InsertOneAsync(doc, cancellationToken: ct);
        }

        private class SnapshotDocument
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string StreamId { get; set; } = string.Empty;
            public string Payload { get; set; } = string.Empty;
            public int Version { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}
