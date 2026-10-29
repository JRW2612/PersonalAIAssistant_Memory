using PersonalAIAssistant.Memory.Core.Models.Safety;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Safety;

/// <summary>
/// Service computing the 8 core evaluation dimensions, heuristic fault detection,
/// belief vs. ground truth divergence, and comparative 15-point rubrics.
/// </summary>
public interface ISafetyEvaluationService
{
    /// <summary>
    /// Computes the complete 8-dimension evaluation report and 15-point rubric for an episode.
    /// </summary>
    SafetyEvaluationReport EvaluateEpisode(
        AgentTrajectoryEpisode episode,
        EvaluationOverrides? overrides = null);

    /// <summary>
    /// Computes side-by-side comparative analysis between two episodes (e.g. Opus vs Sonnet, or Interlocked vs Thin).
    /// </summary>
    ComparativeEvaluationReport CompareEpisodes(
        AgentTrajectoryEpisode episodeA,
        AgentTrajectoryEpisode episodeB);

    /// <summary>
    /// Computes the divergence delta between what the agent believed and what physically occurred.
    /// </summary>
    TurnBeliefDivergence ComputeTurnDivergence(TrajectoryTurn turn);
}
