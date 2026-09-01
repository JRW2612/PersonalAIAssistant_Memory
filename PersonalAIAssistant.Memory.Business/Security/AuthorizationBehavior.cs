using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Business.Security
{
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

        public AuthorizationBehavior(
            IReadModelRepository readRepo,
            ILogger<AuthorizationBehavior<TRequest, TResponse>> logger)
        {
            _readRepo = readRepo;
            _logger = logger;
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
                        // Ownership check
                        if (!string.IsNullOrEmpty(memory.UserId) && memory.UserId != userId)
                        {
                            _logger.LogWarning(
                                "[SECURITY VIOLATION] User {UserId} attempted unauthorized action {Action} on Memory {MemoryId} owned by {OwnerId}",
                                userId, typeof(TRequest).Name, authorizedRequest.MemoryId, memory.UserId);
                            throw new UnauthorizedAccessException(
                                $"User '{userId}' is not authorized to perform action on memory '{authorizedRequest.MemoryId}'.");
                        }

                        // Tenant isolation check
                        if (!string.IsNullOrEmpty(tenantId)
                            && tenantId != "default"
                            && !string.IsNullOrEmpty(memory.TenantId)
                            && memory.TenantId != tenantId)
                        {
                            _logger.LogWarning(
                                "[SECURITY VIOLATION] Cross-tenant access attempt: TenantId={TenantId} tried to access Memory {MemoryId} owned by TenantId={OwnerTenantId}",
                                tenantId, authorizedRequest.MemoryId, memory.TenantId);
                            throw new UnauthorizedAccessException(
                                $"Cross-tenant access denied for memory '{authorizedRequest.MemoryId}'.");
                        }
                    }
                }
            }

            return await next(cancellationToken);
        }
    }
}
