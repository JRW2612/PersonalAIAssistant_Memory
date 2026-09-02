using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Events;
using System.Collections.Concurrent;

namespace PersonalAIAssistant.Memory.Infrastructure.Mongo
{
    public class InMemoryEventStore : IEventStore
    {
        private readonly ConcurrentDictionary<string, List<MemoryEvent>> _streams = new();
        private readonly ConcurrentBag<PersonalAIAssistant.Memory.Core.Messages.OutboxMessage> _outbox = new();

        public Task AppendEventAsync(string streamId, MemoryEvent memoryEvent, int expectedVersion, CancellationToken ct)
        {
            return AppendEventsAsync(streamId, new[] { memoryEvent }, expectedVersion, ct);
        }

        public Task AppendEventsAsync(string streamId, IReadOnlyList<MemoryEvent> events, int expectedVersion, CancellationToken ct)
        {
            if (events == null || events.Count == 0) return Task.CompletedTask;

            _streams.AddOrUpdate(streamId,
                key =>
                {
                    if (expectedVersion != 0)
                    {
                        throw new ConcurrencyException($"Optimistic concurrency failure on stream '{streamId}': expected version {expectedVersion} but found 0.");
                    }
                    var newStream = new List<MemoryEvent>();
                    int ver = 0;
                    foreach (var evt in events)
                    {
                        ver++;
                        evt.Version = ver;
                        evt.Timestamp = DateTime.UtcNow;
                        newStream.Add(evt);
                    }
                    return newStream;
                },
                (key, existingStream) =>
                {
                    lock (existingStream)
                    {
                        var currentVersion = existingStream.Count > 0 ? existingStream.Max(e => e.Version) : 0;
                        if (currentVersion != expectedVersion)
                        {
                            throw new ConcurrencyException($"Optimistic concurrency failure on stream '{streamId}': expected version {expectedVersion} but found {currentVersion}.");
                        }
                        int ver = currentVersion;
                        foreach (var evt in events)
                        {
                            ver++;
                            evt.Version = ver;
                            evt.Timestamp = DateTime.UtcNow;
                            existingStream.Add(evt);
                        }
                    }
                    return existingStream;
                });

            return Task.CompletedTask;
        }

        public async Task<bool> AppendEventsWithOutboxAsync(string streamId, IReadOnlyList<MemoryEvent> events, int expectedVersion, IReadOnlyList<PersonalAIAssistant.Memory.Core.Messages.OutboxMessage>? outboxMessages, CancellationToken ct)
        {
            await AppendEventsAsync(streamId, events, expectedVersion, ct);

            // Store outbox messages in-memory for tests or demo scenarios
            if (outboxMessages != null)
            {
                foreach (var m in outboxMessages)
                    _outbox.Add(m);
            }

            return true;
        }

        public Task<IReadOnlyList<MemoryEvent>> GetEventsAsync(string streamId, CancellationToken ct)
        {
            if (_streams.TryGetValue(streamId, out var stream))
            {
                lock (stream)
                {
                    return Task.FromResult<IReadOnlyList<MemoryEvent>>(stream.OrderBy(e => e.Version).ToList());
                }
            }
            return Task.FromResult<IReadOnlyList<MemoryEvent>>(new List<MemoryEvent>());
        }

        public Task<IReadOnlyList<MemoryEvent>> GetEventsFromVersionAsync(string streamId, int fromVersion, CancellationToken ct)
        {
            if (_streams.TryGetValue(streamId, out var stream))
            {
                lock (stream)
                {
                    return Task.FromResult<IReadOnlyList<MemoryEvent>>(
                        stream.Where(e => e.Version > fromVersion).OrderBy(e => e.Version).ToList());
                }
            }
            return Task.FromResult<IReadOnlyList<MemoryEvent>>(new List<MemoryEvent>());
        }

        public Task<int> GetCurrentVersionAsync(string streamId, CancellationToken ct)
        {
            if (_streams.TryGetValue(streamId, out var stream))
            {
                lock (stream)
                {
                    return Task.FromResult(stream.Count > 0 ? stream.Max(e => e.Version) : 0);
                }
            }
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<(string StreamId, int CurrentVersion)>> GetStreamSummariesAsync(int limit, CancellationToken ct)
        {
            var summaries = new List<(string StreamId, int CurrentVersion)>();
            foreach (var kvp in _streams)
            {
                lock (kvp.Value)
                {
                    var version = kvp.Value.Count > 0 ? kvp.Value.Max(e => e.Version) : 0;
                    summaries.Add((kvp.Key, version));
                }
            }

            var result = summaries.OrderByDescending(s => s.CurrentVersion).Take(limit).ToList();
            return Task.FromResult<IReadOnlyList<(string StreamId, int CurrentVersion)>>(result);
        }
    }
}
