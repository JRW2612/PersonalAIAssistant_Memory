using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PersonalAIAssistant.Memory.Core.Interfaces.Safety;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models.Safety;

namespace PersonalAIAssistant.Memory.Api.Controllers;

public sealed record StartEpisodeRequestDto(
    string TaskId,
    string AgentModel,
    InterlockMode InterlockMode = InterlockMode.Interlocked,
    int MaxStepBudget = 100,
    FaultInjectionDefinition? InjectedFault = null);

public sealed record RecordTurnRequestDto(
    TurnObservation Observation,
    TurnAgentBelief AgentBelief,
    TurnAction Action,
    TurnGroundTruth GroundTruth,
    bool IsInjectedFaultTurn = false);

public sealed record ConcludeEpisodeRequestDto(
    EpisodeClaimedOutcome ClaimedOutcome,
    EpisodeGroundTruthOutcome GroundTruthOutcome);

public sealed record ValidateInterlockRequestDto(
    string ToolName,
    JsonElement Arguments,
    InterlockMode Mode = InterlockMode.Interlocked,
    Dictionary<string, object>? PhysicalState = null);

public sealed record CompareEpisodesRequestDto(
    Guid EpisodeIdA,
    Guid EpisodeIdB);

/// <summary>
/// Physical agent safety evaluation and interlocks API implementing the benchmark
/// methodology from the micro1 research paper ("AI Models Now Introduce Safety Risks in the Physical World").
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/safety")]
public class SafetyEvaluationController : ControllerBase
{
    private readonly IToolInterlockService _interlockService;
    private readonly ITrajectoryEpisodeRepository _episodeRepo;
    private readonly ISafetyEvaluationService _evaluationService;
    private readonly IUserContext _userContext;
    private readonly ISecurityAuditLogger _auditLogger;
    private readonly ILogger<SafetyEvaluationController> _logger;

    public SafetyEvaluationController(
        IToolInterlockService interlockService,
        ITrajectoryEpisodeRepository episodeRepo,
        ISafetyEvaluationService evaluationService,
        IUserContext userContext,
        ISecurityAuditLogger auditLogger,
        ILogger<SafetyEvaluationController> logger)
    {
        _interlockService = interlockService;
        _episodeRepo = episodeRepo;
        _evaluationService = evaluationService;
        _userContext = userContext;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    private string GetCurrentUserId() =>
        _userContext.UserId ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";

    /// <summary>
    /// Initializes a new physical agent evaluation episode with optional fault injection and step budget.
    /// </summary>
    [HttpPost("episodes")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(typeof(AgentTrajectoryEpisode), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartEpisode([FromBody] StartEpisodeRequestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.TaskId))
            return BadRequest("TaskId must not be empty.");
        if (string.IsNullOrWhiteSpace(dto.AgentModel))
            return BadRequest("AgentModel must not be empty.");

        var userId = GetCurrentUserId();
        var episode = new AgentTrajectoryEpisode
        {
            EpisodeId = Guid.NewGuid(),
            TaskId = dto.TaskId,
            AgentModel = dto.AgentModel,
            UserId = userId,
            TenantId = _userContext.TenantId,
            InterlockMode = dto.InterlockMode,
            MaxStepBudget = dto.MaxStepBudget > 0 ? dto.MaxStepBudget : 100,
            InjectedFault = dto.InjectedFault,
            StartedAt = DateTimeOffset.UtcNow,
            Status = EpisodeStatus.Active
        };

        await _episodeRepo.AddEpisodeAsync(episode, ct);
        _logger.LogInformation("Started safety episode {EpisodeId} for task {TaskId} with model {Model} (Mode: {Mode})",
            episode.EpisodeId, episode.TaskId, episode.AgentModel, episode.InterlockMode);

        return CreatedAtAction(nameof(GetEpisode), new { episodeId = episode.EpisodeId }, episode);
    }

    /// <summary>
    /// Records a single turn in the Observe -> Reason -> Act loop, evaluating interlocks and computing belief divergence.
    /// </summary>
    [HttpPost("episodes/{episodeId:guid}/turns")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(typeof(TrajectoryTurn), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordTurn(
        Guid episodeId,
        [FromBody] RecordTurnRequestDto dto,
        CancellationToken ct)
    {
        var episode = await _episodeRepo.GetEpisodeByIdAsync(episodeId, ct);
        if (episode == null)
            return NotFound(new { error = "episode_not_found", message = $"Episode '{episodeId}' not found." });

        var turnIndex = episode.Turns.Count;

        // Evaluate interlocks for this action
        var interlockResult = _interlockService.Evaluate(
            dto.Action.ToolName,
            dto.Action.Arguments,
            episode.InterlockMode,
            dto.GroundTruth.ActualCoordinates);

        var turn = new TrajectoryTurn
        {
            TurnIndex = turnIndex,
            Timestamp = DateTimeOffset.UtcNow,
            Observation = dto.Observation,
            AgentBelief = dto.AgentBelief,
            Action = dto.Action,
            InterlockResult = interlockResult,
            GroundTruth = dto.GroundTruth,
            IsInjectedFaultTurn = dto.IsInjectedFaultTurn || (episode.InjectedFault != null && episode.InjectedFault.InjectionTurn == turnIndex)
        };

        // Compute divergence between agent belief and ground truth
        turn.Divergence = _evaluationService.ComputeTurnDivergence(turn);

        await _episodeRepo.AppendTurnAsync(episodeId, turn, ct);
        return Ok(turn);
    }

    /// <summary>
    /// Concludes an episode with final claimed and ground-truth outcomes.
    /// </summary>
    [HttpPost("episodes/{episodeId:guid}/conclude")]
    [EnableRateLimiting("MemoryWritePolicy")]
    [ProducesResponseType(typeof(AgentTrajectoryEpisode), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConcludeEpisode(
        Guid episodeId,
        [FromBody] ConcludeEpisodeRequestDto dto,
        CancellationToken ct)
    {
        var episode = await _episodeRepo.GetEpisodeByIdAsync(episodeId, ct);
        if (episode == null)
            return NotFound(new { error = "episode_not_found", message = $"Episode '{episodeId}' not found." });

        episode.ClaimedOutcome = dto.ClaimedOutcome;
        episode.GroundTruthOutcome = dto.GroundTruthOutcome;
        episode.CompletedAt = DateTimeOffset.UtcNow;
        episode.Status = dto.ClaimedOutcome.SelfTerminated
            ? EpisodeStatus.Aborted
            : (episode.Turns.Count >= episode.MaxStepBudget ? EpisodeStatus.BudgetExhausted : EpisodeStatus.Completed);

        await _episodeRepo.UpdateEpisodeAsync(episode, ct);
        return Ok(episode);
    }

    /// <summary>
    /// Computes the complete 8-dimension safety evaluation report and 15-point rubric for an episode.
    /// </summary>
    [HttpPost("episodes/{episodeId:guid}/evaluate")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(SafetyEvaluationReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EvaluateEpisode(
        Guid episodeId,
        [FromBody] EvaluationOverrides? overrides,
        CancellationToken ct)
    {
        var episode = await _episodeRepo.GetEpisodeByIdAsync(episodeId, ct);
        if (episode == null)
            return NotFound(new { error = "episode_not_found", message = $"Episode '{episodeId}' not found." });

        var report = _evaluationService.EvaluateEpisode(episode, overrides);
        return Ok(report);
    }

    /// <summary>
    /// Retrieves full trajectory episode details, turn-by-turn history, and divergence tracking.
    /// </summary>
    [HttpGet("episodes/{episodeId:guid}")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(AgentTrajectoryEpisode), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEpisode(Guid episodeId, CancellationToken ct)
    {
        var episode = await _episodeRepo.GetEpisodeByIdAsync(episodeId, ct);
        if (episode == null)
            return NotFound(new { error = "episode_not_found", message = $"Episode '{episodeId}' not found." });

        return Ok(episode);
    }

    /// <summary>
    /// Lists recorded episodes with optional filtering by TaskId and AgentModel.
    /// </summary>
    [HttpGet("episodes")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentTrajectoryEpisode>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListEpisodes(
        [FromQuery] string? taskId,
        [FromQuery] string? agentModel,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var episodes = await _episodeRepo.ListEpisodesAsync(taskId, agentModel, limit, ct);
        return Ok(episodes);
    }

    /// <summary>
    /// Side-by-side comparative evaluation of two episodes (e.g. Opus vs Sonnet, or Interlocked vs Thin).
    /// </summary>
    [HttpPost("evaluate/compare")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(ComparativeEvaluationReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompareEpisodes(
        [FromBody] CompareEpisodesRequestDto dto,
        CancellationToken ct)
    {
        var episodeA = await _episodeRepo.GetEpisodeByIdAsync(dto.EpisodeIdA, ct);
        if (episodeA == null)
            return NotFound(new { error = "episode_a_not_found", message = $"Episode A '{dto.EpisodeIdA}' not found." });

        var episodeB = await _episodeRepo.GetEpisodeByIdAsync(dto.EpisodeIdB, ct);
        if (episodeB == null)
            return NotFound(new { error = "episode_b_not_found", message = $"Episode B '{dto.EpisodeIdB}' not found." });

        var comparativeReport = _evaluationService.CompareEpisodes(episodeA, episodeB);
        return Ok(comparativeReport);
    }

    /// <summary>
    /// Returns all currently registered tool safety interlocks and envelopes.
    /// </summary>
    [HttpGet("interlocks/rules")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(IReadOnlyList<ToolInterlockRule>), StatusCodes.Status200OK)]
    public IActionResult GetInterlockRules()
    {
        var rules = _interlockService.GetRules();
        return Ok(rules);
    }

    /// <summary>
    /// Pre-flight validation testing whether a proposed tool command complies with safety interlocks.
    /// </summary>
    [HttpPost("interlocks/validate")]
    [EnableRateLimiting("MemoryReadPolicy")]
    [ProducesResponseType(typeof(InterlockEvaluationResult), StatusCodes.Status200OK)]
    public IActionResult ValidateInterlock([FromBody] ValidateInterlockRequestDto dto)
    {
        var result = _interlockService.Evaluate(dto.ToolName, dto.Arguments, dto.Mode, dto.PhysicalState);
        return Ok(result);
    }
}
