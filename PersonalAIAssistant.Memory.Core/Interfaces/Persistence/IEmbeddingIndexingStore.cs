namespace PersonalAIAssistant.Memory.Core.Interfaces.Persistence;

public interface IEmbeddingIndexingStore
{
    Task<bool> TryStartAsync(Guid aggregateId, int eventVersion, CancellationToken ct);
    Task MarkCompletedAsync(Guid aggregateId, int eventVersion, CancellationToken ct);
    Task MarkFailedAsync(Guid aggregateId, int eventVersion, string error, CancellationToken ct);
}
