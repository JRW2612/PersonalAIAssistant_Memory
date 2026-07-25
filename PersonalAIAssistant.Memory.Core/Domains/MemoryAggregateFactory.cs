// PersonalAIAssistant.Memory.Core/Domains/MemoryAggregateFactory.cs
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Events;
using System.Text.Json;

namespace PersonalAIAssistant.Memory.Core.Domains
{
    public static class MemoryAggregateFactory
    {
        public static MemoryAggregate RehydrateFromEvents(IEnumerable<MemoryEvent> events)
        {
            var agg = new MemoryAggregate();
            agg.LoadFromHistory(events.OrderBy(e => e.Version));
            return agg;
        }

        public static MemoryAggregate RehydrateFromSnapshot(MemorySnapshotDto snapshotPayload, IEnumerable<MemoryEvent> tailEvents)
        {
            var agg = MemoryAggregate.FromSnapshot(snapshotPayload);
            agg.LoadFromHistory(tailEvents.OrderBy(e => e.Version));
            return agg;
        }

        /// <summary>
        /// Serialises the aggregate's current state into a JSON snapshot payload string.
        /// All fields — including <see cref="MemoryAggregate.Status"/> and
        /// <see cref="MemoryAggregate.Importance"/> — are captured so a round-trip
        /// through <see cref="MemoryAggregate.FromSnapshot"/> is lossless.
        /// </summary>
        public static string CreateSnapshotPayload(MemoryAggregate aggregate)
        {
            var dto = new MemorySnapshotDto
            {
                Id = aggregate.Id.Value,
                Version = aggregate.Version,
                Status = aggregate.Status,
                Importance = aggregate.Importance,
                RawText = aggregate.RawText,
                CompressedText = aggregate.CompressedText,
                ConsolidatedText = aggregate.ConsolidatedText,
                EmbeddingId = aggregate.EmbeddingId,
                Tags = aggregate.Tags.ToList()
            };
            return JsonSerializer.Serialize(dto);
        }
    }
}
