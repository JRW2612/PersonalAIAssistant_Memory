namespace PersonalAIAssistant.Memory.Core.Entities;

public enum AssignmentStatus
{
    Pending = 0,
    Provisioning = 1,
    Active = 2,
    RequiresReconciliation = 3,
    Failed = 4
}

public class AccessAssignmentEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = "default";
    public string TargetResourceId { get; set; } = string.Empty;
    public string PermissionLevel { get; set; } = string.Empty;
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Pending;
    public string IdempotencyKey { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public string? ExternalReferenceId { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProvisionedAtUtc { get; set; }
    public DateTime? LastReconciledAtUtc { get; set; }
}
