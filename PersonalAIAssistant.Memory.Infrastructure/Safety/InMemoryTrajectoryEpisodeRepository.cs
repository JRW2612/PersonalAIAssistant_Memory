using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.Safety;
using PersonalAIAssistant.Memory.Core.Models.Safety;

namespace PersonalAIAssistant.Memory.Infrastructure.Safety;

/// <summary>
/// Thread-safe in-memory store for Agent Trajectory Episodes.
/// Bounded capacity with FIFO eviction to prevent memory exhaustion in high-throughput testing.
/// </summary>
public sealed class InMemoryTrajectoryEpisodeRepository : ITrajectoryEpisodeRepository
{
    private const int MaxEpisodes = 5000;
    private readonly ConcurrentDictionary<Guid, AgentTrajectoryEpisode> _episodes = new();
    private readonly ConcurrentQueue<Guid> _insertionOrder = new();
    private readonly ILogger<InMemoryTrajectoryEpisodeRepository> _logger;

    public InMemoryTrajectoryEpisodeRepository(ILogger<InMemoryTrajectoryEpisodeRepository> logger)
    {
        _logger = logger;
    }

    public Task AddEpisodeAsync(AgentTrajectoryEpisode episode, CancellationToken ct = default)
    {
        while (_episodes.Count >= MaxEpisodes)
        {
            if (_insertionOrder.TryDequeue(out var evictId))
            {
                _episodes.TryRemove(evictId, out _);
            }
            else
            {
                break;
            }
        }

        if (_episodes.TryAdd(episode.EpisodeId, episode))
        {
            _insertionOrder.Enqueue(episode.EpisodeId);
        }

        return Task.CompletedTask;
    }

    public Task<AgentTrajectoryEpisode?> GetEpisodeByIdAsync(Guid episodeId, CancellationToken ct = default)
    {
        _episodes.TryGetValue(episodeId, out var episode);
        return Task.FromResult(episode);
    }

    public Task AppendTurnAsync(Guid episodeId, TrajectoryTurn turn, CancellationToken ct = default)
    {
        if (_episodes.TryGetValue(episodeId, out var episode))
        {
            lock (episode.Turns)
            {
                episode.Turns.Add(turn);
            }
        }
        else
        {
            _logger.LogWarning("Attempted to append turn to non-existent episode '{EpisodeId}'.", episodeId);
        }
        return Task.CompletedTask;
    }

    public Task UpdateEpisodeAsync(AgentTrajectoryEpisode episode, CancellationToken ct = default)
    {
        _episodes[episode.EpisodeId] = episode;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AgentTrajectoryEpisode>> ListEpisodesAsync(
        string? taskId = null,
        string? agentModel = null,
        int limit = 20,
        CancellationToken ct = default)
    {
        var query = _episodes.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(taskId))
        {
            query = query.Where(e => e.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(agentModel))
        {
            query = query.Where(e => e.AgentModel.Equals(agentModel, StringComparison.OrdinalIgnoreCase));
        }

        var results = query
            .OrderByDescending(e => e.StartedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();

        return Task.FromResult<IReadOnlyList<AgentTrajectoryEpisode>>(results);
    }
}
