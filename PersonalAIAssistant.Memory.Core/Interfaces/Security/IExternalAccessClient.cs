namespace PersonalAIAssistant.Memory.Core.Interfaces.Security;

public record ExternalGrantResult(string GrantId, bool IsActive);

public interface IExternalAccessClient
{
    Task<ExternalGrantResult> GrantAccessAsync(string userId, string targetResourceId, string permissionLevel, string idempotencyKey, CancellationToken ct = default);
    Task<ExternalGrantResult?> QueryGrantByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
}
