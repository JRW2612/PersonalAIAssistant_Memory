using Microsoft.EntityFrameworkCore;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Events;
using PersonalAIAssistant.Memory.Infrastructure.EF.Entities;

namespace PersonalAIAssistant.Memory.Infrastructure.EF
{
    public class EfEventStore : IEventStore
    {
        private readonly EventStoreDbContext _db;

        public EfEventStore(EventStoreDbContext db)
        {
            _db = db;
        }

        public async Task AppendEventAsync(string streamId, MemoryEvent memoryEvent, int expectedVersion, CancellationToken ct)
        {
            await AppendEventsAsync(streamId, new[] { memoryEvent }, expectedVersion, ct);
        }

        public async Task AppendEventsAsync(string streamId, IReadOnlyList<MemoryEvent> events, int expectedVersion, CancellationToken ct)
        {
            if (events == null || events.Count == 0) return;

            // Determine current version
            var last = await _db.Events.Where(e => e.StreamId == streamId).OrderByDescending(e => e.Version).FirstOrDefaultAsync(ct);
            var currentVersion = last?.Version ?? 0;
            if (currentVersion != expectedVersion)
                throw new ConcurrencyException($"Optimistic concurrency failure on stream '{streamId}': expected version {expectedVersion} but found {currentVersion}.");

            var version = currentVersion;
            foreach (var evt in events)
            {
                version++;
                evt.Version = version;
                evt.Timestamp = DateTime.UtcNow;
                var ent = new EventEntity
                {
                    StreamId = streamId,
                    EventId = evt.EventId,
                    Version = evt.Version,
                    Timestamp = evt.Timestamp,
                    EventType = evt.GetType().Name,
                    Payload = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType()),
                    AggregateId = evt.AggregateId.ToString(),
                    UserId = evt.UserId ?? string.Empty,
                    IsEncrypted = false
                };
                _db.Events.Add(ent);
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task<bool> AppendEventsWithOutboxAsync(string streamId, IReadOnlyList<MemoryEvent> events, int expectedVersion, IReadOnlyList<PersonalAIAssistant.Memory.Core.Messages.OutboxMessage>? outboxMessages, CancellationToken ct)
        {
            if ((events == null || events.Count == 0) && (outboxMessages == null || outboxMessages.Count == 0)) return true;

            using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // append events
                if (events != null && events.Count > 0)
                {
                    var last = await _db.Events.Where(e => e.StreamId == streamId).OrderByDescending(e => e.Version).FirstOrDefaultAsync(ct);
                    var currentVersion = last?.Version ?? 0;
                    if (currentVersion != expectedVersion)
                        throw new ConcurrencyException($"Optimistic concurrency failure on stream '{streamId}': expected version {expectedVersion} but found {currentVersion}.");

                    var version = currentVersion;
                    foreach (var evt in events)
                    {
                        version++;
                        evt.Version = version;
                        evt.Timestamp = DateTime.UtcNow;
                        var ent = new EventEntity
                        {
                            StreamId = streamId,
                            EventId = evt.EventId,
                            Version = evt.Version,
                            Timestamp = evt.Timestamp,
                            EventType = evt.GetType().Name,
                            Payload = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType()),
                            AggregateId = evt.AggregateId.ToString(),
                            UserId = evt.UserId ?? string.Empty,
                            IsEncrypted = false
                        };
                        _db.Events.Add(ent);
                    }
                }

                if (outboxMessages != null && outboxMessages.Count > 0)
                {
                    foreach (var m in outboxMessages)
                    {
                        var outEnt = new EfOutboxMessage
                        {
                            MessageId = m.MessageId,
                            MessageType = m.MessageType,
                            Payload = m.Payload,
                            OccurredAt = m.OccurredAt,
                            Attempts = 0
                        };
                        _db.OutboxMessages.Add(outEnt);
                    }
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        public async Task<IReadOnlyList<MemoryEvent>> GetEventsAsync(string streamId, CancellationToken ct)
        {
            var docs = await _db.Events.Where(e => e.StreamId == streamId).OrderBy(e => e.Version).ToListAsync(ct);
            return docs.Select(d =>
            {
                var t = System.Text.Json.JsonSerializer.Deserialize(d.Payload, typeof(MemoryEvent), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (t is MemoryEvent me)
                {
                    me.EventId = d.EventId;
                    me.Version = d.Version;
                    me.Timestamp = d.Timestamp;
                    return me;
                }
                return null;
            }).Where(x => x != null)!.Select(x => x!).ToList();
        }

        public async Task<IReadOnlyList<MemoryEvent>> GetEventsFromVersionAsync(string streamId, int fromVersion, CancellationToken ct)
        {
            var docs = await _db.Events.Where(e => e.StreamId == streamId && e.Version > fromVersion).OrderBy(e => e.Version).ToListAsync(ct);
            return docs.Select(d =>
            {
                var t = System.Text.Json.JsonSerializer.Deserialize(d.Payload, typeof(MemoryEvent), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (t is MemoryEvent me)
                {
                    me.EventId = d.EventId;
                    me.Version = d.Version;
                    me.Timestamp = d.Timestamp;
                    return me;
                }
                return null;
            }).Where(x => x != null)!.Select(x => x!).ToList();
        }

        public async Task<int> GetCurrentVersionAsync(string streamId, CancellationToken ct)
        {
            var last = await _db.Events.Where(e => e.StreamId == streamId).OrderByDescending(e => e.Version).FirstOrDefaultAsync(ct);
            return last?.Version ?? 0;
        }

        public async Task<IReadOnlyList<(string StreamId, int CurrentVersion)>> GetStreamSummariesAsync(int limit, CancellationToken ct)
        {
            var summaries = await _db.Events.GroupBy(e => e.StreamId).Select(g => new { StreamId = g.Key, CurrentVersion = g.Max(x => x.Version) }).OrderByDescending(x => x.CurrentVersion).Take(limit).ToListAsync(ct);
            return summaries.Select(s => (s.StreamId, s.CurrentVersion)).ToList();
        }
    }
}
