// PersonalAIAssistant.Memory.Infrastructure.Mongo/MongoEventStore.cs
using MongoDB.Driver;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Infrastructure.Mongo
{
    public class MongoEventStore : IEventStore
    {
        private readonly IMongoCollection<EventDocument> _collection;

        public MongoEventStore(IMongoDatabase database, string collectionName = "events")
        {
            _collection = database.GetCollection<EventDocument>(collectionName);
            // ensure index on StreamId + Version for fast reads and uniqueness
            var indexKeys = Builders<EventDocument>.IndexKeys.Ascending(d => d.StreamId).Ascending(d => d.Version);
            _collection.Indexes.CreateOne(new CreateIndexModel<EventDocument>(indexKeys, new CreateIndexOptions { Unique = true }));
        }

        public async Task AppendEventAsync(string streamId, MemoryEvent memoryEvent, int expectedVersion, CancellationToken ct)
        {
            await AppendEventsAsync(streamId, new[] { memoryEvent }, expectedVersion, ct);
        }

        public async Task AppendEventsAsync(string streamId, IReadOnlyList<MemoryEvent> events, int expectedVersion, CancellationToken ct)
        {
            if (events == null || events.Count == 0) return;

            // get current version
            var filter = Builders<EventDocument>.Filter.Eq(d => d.StreamId, streamId);
            var sort = Builders<EventDocument>.Sort.Descending(d => d.Version);
            var last = await _collection.Find(filter).Sort(sort).Limit(1).FirstOrDefaultAsync(ct);
            var currentVersion = last?.Version ?? 0;

            if (currentVersion != expectedVersion)
                throw new ConcurrencyException($"Expected version {expectedVersion}, but found {currentVersion}");

            var docs = new List<EventDocument>();
            var version = currentVersion;
            foreach (var evt in events)
            {
                version++;
                evt.Version = version;
                evt.Timestamp = DateTime.UtcNow;
                docs.Add(EventDocument.FromMemoryEvent(streamId, evt));
            }

            await _collection.InsertManyAsync(docs, cancellationToken: ct);
        }

        public async Task<IReadOnlyList<MemoryEvent>> GetEventsAsync(string streamId, CancellationToken ct)
        {
            var filter = Builders<EventDocument>.Filter.Eq(d => d.StreamId, streamId);
            var docs = await _collection.Find(filter).SortBy(d => d.Version).ToListAsync(ct);
            return docs.Select(d => d.ToMemoryEvent()).ToList();
        }

        public async Task<IReadOnlyList<MemoryEvent>> GetEventsFromVersionAsync(string streamId, int fromVersion, CancellationToken ct)
        {
            var filter = Builders<EventDocument>.Filter.And(
                Builders<EventDocument>.Filter.Eq(d => d.StreamId, streamId),
                Builders<EventDocument>.Filter.Gt(d => d.Version, fromVersion)
            );
            var docs = await _collection.Find(filter).SortBy(d => d.Version).ToListAsync(ct);
            return docs.Select(d => d.ToMemoryEvent()).ToList();
        }

        public async Task<int> GetCurrentVersionAsync(string streamId, CancellationToken ct)
        {
            var filter = Builders<EventDocument>.Filter.Eq(d => d.StreamId, streamId);
            var sort = Builders<EventDocument>.Sort.Descending(d => d.Version);
            var last = await _collection.Find(filter).Sort(sort).Limit(1).FirstOrDefaultAsync(ct);
            return last?.Version ?? 0;
        }

        // Internal document mapping
        private class EventDocument
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string StreamId { get; set; } = string.Empty;
            public Guid EventId { get; set; }
            public int Version { get; set; }
            public DateTime Timestamp { get; set; }
            public string EventType { get; set; } = string.Empty;
            public string Payload { get; set; } = string.Empty; // serialized MemoryEvent payload (JSON)
            public string AggregateId { get; set; } = string.Empty;

            // Dynamically build a map of "EventName" -> Type upon initialization
            // This completely eliminates magic strings and switch statements.
            private static readonly Dictionary<string, Type> EventTypeMap = typeof(MemoryEvent).Assembly
                .GetTypes()
                .Where(t => t.IsSubclassOf(typeof(MemoryEvent)) && !t.IsAbstract)
                .ToDictionary(t => t.Name, t => t);

            public static EventDocument FromMemoryEvent(string streamId, MemoryEvent evt)
            {
                return new EventDocument
                {
                    StreamId = streamId,
                    EventId = evt.EventId,
                    Version = evt.Version,
                    Timestamp = evt.Timestamp,
                    EventType = evt.GetType().Name, // Safely stores "MemoryAddedEvent" etc.
                    Payload = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType()), // Serialize as concrete type
                    AggregateId = evt.AggregateId.ToString()
                };
            }

            public MemoryEvent ToMemoryEvent()
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // 1. Look up the specific class type dynamically
                if (!EventTypeMap.TryGetValue(EventType, out var specificType))
                {
                    // Fallback to base type if the class isn't found
                    specificType = typeof(MemoryEvent);
                }

                // 2. Deserialize directly into the specific type
                var evt = (MemoryEvent?)System.Text.Json.JsonSerializer.Deserialize(Payload, specificType, options);

                if (evt != null)
                {
                    // 3. Restore metadata tracked by the document wrapper
                    evt.EventId = EventId;
                    evt.Version = Version;
                    evt.Timestamp = Timestamp;
                    return evt;
                }

                throw new InvalidOperationException($"Failed to deserialize event payload for type {EventType}.");
            }
        }
    }
}
