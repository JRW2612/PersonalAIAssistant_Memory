using PersonalAIAssistant.Memory.Events;
namespace PersonalAIAssistant.Memory.Infrastructure.Mongo
{
    public class MongoEventStore : IEventStore
    {
        public Task AppendEventAsync(MemoryEvent memoryEvent, string streamId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<MemoryEvent>> GetEventsAsync(string streamId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
