using PersonalAIAssistant.Memory.Core.DTOs;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Others
{
    public interface ISnapshotRepository
    {
        Task<IEnumerable<string>> GetStreamsNeedingSnapshotAsync(int eventThreshold, int batchSize, CancellationToken ct);
        Task<MemorySnapshotDto?> GetLatestSnapshotAsync(string streamId, CancellationToken ct);
        Task SaveSnapshotAsync(string streamId, string payload, int version, CancellationToken ct);
    }
}
