using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Infrastructure.Security;

namespace PersonalAIAssistant.Memory.Api.Controllers;

[ApiController]
[Route("api/v1/admin/dlq")]
[Authorize(Roles = "Admin,Administrator,ComplianceAuditor")]
public class DlqReplayController : ControllerBase
{
    private readonly IAccessAssignmentRepository _repository;
    private readonly AccessAssignmentConsumer _consumer;
    private readonly ILogger<DlqReplayController> _logger;

    public DlqReplayController(
        IAccessAssignmentRepository repository,
        AccessAssignmentConsumer consumer,
        ILogger<DlqReplayController> logger)
    {
        _repository = repository;
        _consumer = consumer;
        _logger = logger;
    }

    /// <summary>
    /// Controlled replay of failed/dead-lettered access assignments after investigation.
    /// </summary>
    [HttpPost("replay/{assignmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplayAssignment(Guid assignmentId, CancellationToken ct)
    {
        var assignment = await _repository.GetByIdAsync(assignmentId, ct);
        if (assignment == null)
        {
            return NotFound(new { error = $"Assignment '{assignmentId}' was not found." });
        }

        if (assignment.Status != AssignmentStatus.Failed && assignment.Status != AssignmentStatus.RequiresReconciliation)
        {
            return BadRequest(new { error = $"Assignment is in status '{assignment.Status}' and does not qualify for replay." });
        }

        assignment.Status = AssignmentStatus.Pending;
        assignment.RetryCount = 0;
        assignment.LastError = $"Replay triggered by administrator '{User.Identity?.Name}' at {DateTime.UtcNow:o}";
        await _repository.UpdateAsync(assignment, ct);

        var payload = JsonSerializer.Serialize(new
        {
            AssignmentId = assignment.Id,
            assignment.UserId,
            assignment.TargetResourceId,
            assignment.PermissionLevel,
            assignment.IdempotencyKey
        });

        // Trigger consumer processing
        await _consumer.ProcessAssignmentAsync(payload, ct);

        _logger.LogInformation("Replayed dead-letter assignment {AssignmentId}", assignment.Id);
        return Ok(new { message = "Assignment replayed successfully.", assignmentId = assignment.Id });
    }
}
