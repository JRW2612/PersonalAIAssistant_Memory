using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Api.Controllers;

public record CreateAssignmentRequest(string UserId, string TargetResourceId, string PermissionLevel);

[ApiController]
[Route("api/v1/access-assignments")]
[Authorize]
public class AccessAssignmentsController : ControllerBase
{
    private readonly IAccessAssignmentRepository _repository;
    private readonly ILogger<AccessAssignmentsController> _logger;

    public AccessAssignmentsController(
        IAccessAssignmentRepository repository,
        ILogger<AccessAssignmentsController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Creates an access assignment using the Transactional Outbox pattern.
    /// Atomically persists the assignment and outbox event in a single transaction.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAssignment(
        [FromBody] CreateAssignmentRequest request, 
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.TargetResourceId))
        {
            return BadRequest("UserId and TargetResourceId are required.");
        }

        var assignmentId = Guid.NewGuid();
        var idempotencyKey = $"grant:{request.UserId}:{request.TargetResourceId}:{request.PermissionLevel}:{DateTime.UtcNow:yyyyMMdd}";

        var assignment = new AccessAssignmentEntity
        {
            Id = assignmentId,
            UserId = request.UserId,
            TargetResourceId = request.TargetResourceId,
            PermissionLevel = request.PermissionLevel,
            Status = AssignmentStatus.Pending,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = DateTime.UtcNow
        };

        var payload = JsonSerializer.Serialize(new
        {
            AssignmentId = assignment.Id,
            assignment.UserId,
            assignment.TargetResourceId,
            assignment.PermissionLevel,
            assignment.IdempotencyKey
        });

        // Single atomic database transaction (Dual-write solved)
        var success = await _repository.CreateAssignmentWithOutboxAsync(
            assignment, 
            "AccessAssignmentCreatedEvent", 
            payload, 
            ct);

        if (!success)
        {
            return StatusCode(500, "Failed to commit access assignment outbox transaction.");
        }

        _logger.LogInformation("Committed access assignment {AssignmentId} with outbox event atomically.", assignment.Id);
        return AcceptedAtAction(nameof(GetStatus), new { id = assignment.Id }, new { assignment.Id, assignment.Status });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AccessAssignmentEntity), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken ct)
    {
        var item = await _repository.GetByIdAsync(id, ct);
        return item is not null ? Ok(item) : NotFound();
    }
}
