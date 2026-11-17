namespace PersonalAIAssistant.Memory.Api.DTOs;

/// <summary>
/// Data Transfer Object representing an access assignment.
/// Prevents over-exposure of internal orchestration entities by omitting sensitive backend state
/// such as IdempotencyKey, RetryCount, LastError, and internal reconciliation timestamps.
/// </summary>
public record AccessAssignmentResponseDto(
    Guid Id,
    string UserId,
    string TenantId,
    string TargetResourceId,
    string PermissionLevel,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ProvisionedAtUtc
);
