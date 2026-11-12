using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Business.Security;

/// <summary>
/// MediatR pipeline behavior enforcing ownership and tenant isolation.
/// SRP: only handles resource-level authorization. OCP: new request types
/// opt-in by implementing IAuthorizedRequest.
/// </summary>
public class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IReadModelRepository _readRepo;
    private readonly ILogger<AuthorizationBehavior<TRequest, TResponse>> _logger;
    private readonly ISecurityAuditLogger? _auditLogger;

    public AuthorizationBehavior(
        IReadModelRepository readRepo,
        ILogger<AuthorizationBehavior<TRequest, TResponse>> logger,
        ISecurityAuditLogger? auditLogger = null)
    {
        _readRepo = readRepo;
        _logger = logger;
        _auditLogger = auditLogger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is IAuthorizedRequest authorizedRequest)
        {
            var userId = authorizedRequest.UserId;
            var tenantId = authorizedRequest.TenantId;

            _logger.LogInformation(
                "Authorizing {RequestType} for MemoryId={MemoryId} UserId={UserId} TenantId={TenantId}",
                typeof(TRequest).Name, authorizedRequest.MemoryId, userId, tenantId);

            if (!string.IsNullOrEmpty(userId) && userId != "system")
            {
                var memories = await _readRepo.GetMemoriesByIdsAsync(
                    new[] { authorizedRequest.MemoryId }, cancellationToken);
                var memory = memories.FirstOrDefault();

                if (memory != null)
                {
                    // Tenant isolation check (highest priority security boundary)
                    if (!string.IsNullOrEmpty(tenantId)
                        && tenantId != "default"
                        && !string.IsNullOrEmpty(memory.TenantId)
                        && memory.TenantId != tenantId)
                    {
                        _logger.LogWarning(
                            "[SECURITY VIOLATION] Cross-tenant access attempt: TenantId={TenantId} tried to access Memory {MemoryId} owned by TenantId={OwnerTenantId}",
                            tenantId, authorizedRequest.MemoryId, memory.TenantId);
                        _auditLogger?.LogCrossTenantAttempt(userId, tenantId, authorizedRequest.MemoryId.ToString(), memory.TenantId, typeof(TRequest).Name);
                        throw new UnauthorizedAccessException(
                            $"Cross-tenant access denied for memory '{authorizedRequest.MemoryId}'.");
                    }

                    // Ownership check. Missing ownership data is unverifiable and must fail closed.
                    if (string.IsNullOrEmpty(memory.UserId))
                    {
                        _logger.LogWarning(
                            "Authorization denied because memory {MemoryId} has no owner in the read model",
                            authorizedRequest.MemoryId);
                        _auditLogger?.LogAccessDenied(userId, typeof(TRequest).Name, authorizedRequest.MemoryId.ToString(), "Memory ownership could not be verified");
                        throw new UnauthorizedAccessException(
                            $"Access denied for memory '{authorizedRequest.MemoryId}' because ownership could not be verified.");
                    }

                    if (memory.UserId != userId)
                    {
                        _logger.LogWarning(
                            "[SECURITY VIOLATION] User {UserId} attempted unauthorized action {Action} on Memory {MemoryId} owned by {OwnerId}",
                            userId, typeof(TRequest).Name, authorizedRequest.MemoryId, memory.UserId);
                        _auditLogger?.LogAccessDenied(userId, typeof(TRequest).Name, authorizedRequest.MemoryId.ToString(), "Ownership violation");
                        throw new UnauthorizedAccessException(
                            $"User '{userId}' is not authorized to perform action on memory '{authorizedRequest.MemoryId}'.");
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Authorization denied because memory {MemoryId} is missing from the read model for user {UserId}",
                        authorizedRequest.MemoryId, userId);
                    _auditLogger?.LogAccessDenied(userId, typeof(TRequest).Name, authorizedRequest.MemoryId.ToString(), "Memory ownership could not be verified");
                    throw new UnauthorizedAccessException(
                        $"Access denied for memory '{authorizedRequest.MemoryId}' because ownership could not be verified.");
                }
            }
        }

        return await next(cancellationToken);
    }
}
