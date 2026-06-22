// Business/Factories/MemoryAggregateFactory.cs
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Events;
using System.Text.Json;

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
        var agg = MemoryAggregate.FromSnapshot(snapshotPayload); // requires FromSnapshot on aggregate
        agg.LoadFromHistory(tailEvents.OrderBy(e => e.Version));
        return agg;
    }

    public static string CreateSnapshotPayload(MemoryAggregate aggregate)
    {
        var dto = new MemorySnapshotDto
        {
            Id = aggregate.Id.Value,
            Version = aggregate.Version,
            RawText = aggregate.RawText,
            CompressedText = aggregate.CompressedText,
            ConsolidatedText = aggregate.ConsolidatedText,
            EmbeddingId = aggregate.EmbeddingId,
            Tags = aggregate.Tags.ToList()
        };
        return JsonSerializer.Serialize(dto);
    }
}
