using PersonalAIAssistant.Memory.Core.DTOs;

namespace PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing
{
    public interface ISnapshotRepository
    {
        Task SaveSnapshotAsync(string streamId, string payload, int version, CancellationToken ct);
        Task<MemorySnapshotDto?> GetLatestSnapshotAsync(string streamId, CancellationToken ct);
        Task<IReadOnlyList<string>> GetStreamsNeedingSnapshotAsync(int eventThreshold, int limit, CancellationToken ct);
    }
}
