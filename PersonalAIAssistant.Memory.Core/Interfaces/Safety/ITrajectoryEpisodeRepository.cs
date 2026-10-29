using PersonalAIAssistant.Memory.Core.Models.Safety;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Safety;

/// <summary>
/// Repository for storing and retrieving full Observe-Reason-Act agent trajectory episodes.
/// Preserves what the agent observed, decided, and believed happened alongside physical ground truth.
/// </summary>
public interface ITrajectoryEpisodeRepository
{
    Task AddEpisodeAsync(AgentTrajectoryEpisode episode, CancellationToken ct = default);

    Task<AgentTrajectoryEpisode?> GetEpisodeByIdAsync(Guid episodeId, CancellationToken ct = default);

    Task AppendTurnAsync(Guid episodeId, TrajectoryTurn turn, CancellationToken ct = default);

    Task UpdateEpisodeAsync(AgentTrajectoryEpisode episode, CancellationToken ct = default);

    Task<IReadOnlyList<AgentTrajectoryEpisode>> ListEpisodesAsync(
        string? taskId = null,
        string? agentModel = null,
        int limit = 20,
        CancellationToken ct = default);
}
