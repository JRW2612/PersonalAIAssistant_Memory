using PersonalAIAssistant.Memory.Core.Messages;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Business.Handlers;

internal static class EventOutboxMessageFactory
{
    public static IReadOnlyList<OutboxMessage> Create(IEnumerable<MemoryEvent> events)
        => events.Select(evt => new OutboxMessage
        {
            MessageId = evt.EventId,
            MessageType = evt.GetType().Name,
            Payload = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType()),
            OccurredAt = DateTime.UtcNow
        }).ToList();
}
