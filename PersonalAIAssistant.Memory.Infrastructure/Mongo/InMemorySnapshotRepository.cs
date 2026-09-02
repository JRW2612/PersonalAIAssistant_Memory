using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using System.Collections.Concurrent;
using System.Text.Json;

namespace PersonalAIAssistant.Memory.Infrastructure.Mongo
{
    /// <summary>
    /// Thread-safe in-memory snapshot repository implementation for testing, demo, and fallback.
    /// </summary>
    public class InMemorySnapshotRepository : ISnapshotRepository
    {
        private readonly ConcurrentDictionary<string, List<SnapshotItem>> _snapshots = new();
        private readonly IEventStore _eventStore;

        public InMemorySnapshotRepository(IEventStore eventStore)
        {
            _eventStore = eventStore;
        }

        public async Task<IReadOnlyList<string>> GetStreamsNeedingSnapshotAsync(int eventThreshold, int batchSize, CancellationToken ct)
        {
            var streamSummaries = await _eventStore.GetStreamSummariesAsync(batchSize * 3, ct);
            var result = new List<string>();

            foreach (var (streamId, currentVersion) in streamSummaries)
            {
                if (result.Count >= batchSize) break;

                int latestVersion = 0;
                if (_snapshots.TryGetValue(streamId, out var items))
                {
                    lock (items)
                    {
                        latestVersion = items.Count > 0 ? items.Max(i => i.Version) : 0;
                    }
                }

                if (currentVersion - latestVersion >= eventThreshold)
                {
                    result.Add(streamId);
                }
            }

            return result;
        }

        public Task<MemorySnapshotDto?> GetLatestSnapshotAsync(string streamId, CancellationToken ct)
        {
            if (_snapshots.TryGetValue(streamId, out var items))
            {
                lock (items)
                {
                    var latest = items.OrderByDescending(i => i.Version).FirstOrDefault();
                    if (latest != null)
                    {
                        var dto = JsonSerializer.Deserialize<MemorySnapshotDto>(latest.Payload,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        return Task.FromResult(dto);
                    }
                }
            }
            return Task.FromResult<MemorySnapshotDto?>(null);
        }

        public Task SaveSnapshotAsync(string streamId, string payload, int version, CancellationToken ct)
        {
            var item = new SnapshotItem
            {
                StreamId = streamId,
                Payload = payload,
                Version = version,
                CreatedAt = DateTime.UtcNow
            };

            _snapshots.AddOrUpdate(streamId,
                key => new List<SnapshotItem> { item },
                (key, existing) =>
                {
                    lock (existing)
                    {
                        existing.Add(item);
                    }
                    return existing;
                });

            return Task.CompletedTask;
        }

        private class SnapshotItem
        {
            public string StreamId { get; set; } = string.Empty;
            public string Payload { get; set; } = string.Empty;
            public int Version { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}
