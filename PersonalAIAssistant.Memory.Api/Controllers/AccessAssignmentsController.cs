using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalAIAssistant.Memory.Api.DTOs;
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
    private readonly IUserContext _userContext;
    private readonly ILogger<AccessAssignmentsController> _logger;

    public AccessAssignmentsController(
        IAccessAssignmentRepository repository,
        IUserContext userContext,
        ILogger<AccessAssignmentsController> logger)
    {
        _repository = repository;
        _userContext = userContext;
        _logger = logger;
    }

    /// <summary>
    /// Creates an access assignment using the Transactional Outbox pattern.
    /// Atomically persists the assignment and outbox event in a single transaction.
    /// Defends against IDOR / BOLA by requiring caller to own the assignment unless holding an administrative role.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateAssignment(
        [FromBody] CreateAssignmentRequest request, 
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.TargetResourceId))
        {
            return BadRequest("UserId and TargetResourceId are required.");
        }

        // BOLA / IDOR defense: Prevent unprivileged users from granting permissions on behalf of or targeting arbitrary user IDs
        var isAdmin = _userContext.IsAdmin || _userContext.Roles.Contains("SecurityAdmin", StringComparer.OrdinalIgnoreCase);
        if (!isAdmin && !string.Equals(request.UserId, _userContext.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var assignmentId = Guid.NewGuid();
        var idempotencyKey = $"grant:{request.UserId}:{request.TargetResourceId}:{request.PermissionLevel}:{DateTime.UtcNow:yyyyMMdd}";
        var tenantId = !string.IsNullOrWhiteSpace(_userContext.TenantId) ? _userContext.TenantId : "default";

        var assignment = new AccessAssignmentEntity
        {
            Id = assignmentId,
            UserId = request.UserId,
            TenantId = tenantId,
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
            assignment.TenantId,
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

    /// <summary>
    /// Retrieves status of an access assignment.
    /// Defends against BOLA by ensuring non-admins can only inspect their own assignments.
    /// Defends against entity over-exposure by mapping internal entity to AccessAssignmentResponseDto.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AccessAssignmentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken ct)
    {
        var item = await _repository.GetByIdAsync(id, ct);
        if (item is null) return NotFound();

        // BOLA / IDOR defense
        var isAdmin = _userContext.IsAdmin || _userContext.Roles.Contains("SecurityAdmin", StringComparer.OrdinalIgnoreCase);
        if (!isAdmin && !string.Equals(item.UserId, _userContext.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var dto = new AccessAssignmentResponseDto(
            item.Id,
            item.UserId,
            item.TenantId,
            item.TargetResourceId,
            item.PermissionLevel,
            item.Status.ToString(),
            item.CreatedAtUtc,
            item.ProvisionedAtUtc
        );

        return Ok(dto);
    }
}
