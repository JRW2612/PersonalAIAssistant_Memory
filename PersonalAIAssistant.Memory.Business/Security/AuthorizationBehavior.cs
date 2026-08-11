using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.Sql;

namespace PersonalAIAssistant.Memory.Business.Security
{
    public class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        private readonly IReadModelRepository _readRepo;
        private readonly ILogger<AuthorizationBehavior<TRequest, TResponse>> _logger;

        public AuthorizationBehavior(IReadModelRepository readRepo, ILogger<AuthorizationBehavior<TRequest, TResponse>> logger)
        {
            _readRepo = readRepo;
            _logger = logger;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (request is IAuthorizedRequest authorizedRequest)
            {
                _logger.LogInformation("Authorizing request {RequestType} for MemoryId {MemoryId} by UserId {UserId}", 
                    typeof(TRequest).Name, authorizedRequest.MemoryId, authorizedRequest.UserId);

                if (!string.IsNullOrEmpty(authorizedRequest.UserId) && authorizedRequest.UserId != "system")
                {
                    var memories = await _readRepo.GetMemoriesByIdsAsync(new[] { authorizedRequest.MemoryId }, cancellationToken);
                    var memory = memories.FirstOrDefault();

                    if (memory != null && !string.IsNullOrEmpty(memory.UserId) && memory.UserId != authorizedRequest.UserId)
                    {
                        _logger.LogWarning("[SECURITY VIOLATION] User {UserId} attempted unauthorized action {Action} on Memory {MemoryId} owned by {OwnerId}",
                            authorizedRequest.UserId, typeof(TRequest).Name, authorizedRequest.MemoryId, memory.UserId);
                        throw new UnauthorizedAccessException($"User '{authorizedRequest.UserId}' is not authorized to perform action on memory '{authorizedRequest.MemoryId}'.");
                    }
                }
            }

            return await next(cancellationToken);
        }
    }
}
