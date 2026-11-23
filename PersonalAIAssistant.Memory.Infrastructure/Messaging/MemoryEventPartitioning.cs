namespace PersonalAIAssistant.Memory.Infrastructure.Messaging;

public static class MemoryEventPartitioning
{
    public const int PartitionCount = 8;
    private const string QueuePrefix = "memory-event-partition-";

    public static int GetPartition(Guid aggregateId)
    {
        var hash = 2166136261u;
        foreach (var value in aggregateId.ToByteArray())
        {
            hash = (hash ^ value) * 16777619u;
        }

        return (int)(hash % PartitionCount);
    }

    public static string GetQueueName(Guid aggregateId)
        => GetQueueName(GetPartition(aggregateId));

    public static string GetQueueName(int partition)
    {
        if ((uint)partition >= PartitionCount)
        {
            throw new ArgumentOutOfRangeException(nameof(partition));
        }

        return $"{QueuePrefix}{partition}";
    }
}
