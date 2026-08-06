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
                // In a real system, you would check if the memory belongs to the user.
                // The Read Model might not have UserId indexed yet, but this is the architectural hook.
                // We assume there's a way to check ownership. Since MemoryReadModel doesn't have UserId currently,
                // we would normally load the aggregate or query a specific ownership view.
                
                // For this demo architecture, we just log and pass through, or we could simulate it.
                _logger.LogInformation("Authorizing request {RequestType} for MemoryId {MemoryId} by UserId {UserId}", 
                    typeof(TRequest).Name, authorizedRequest.MemoryId, authorizedRequest.UserId);

                // TODO: Actual DB check against ownership
                // var isOwner = await _readRepo.IsOwnerAsync(authorizedRequest.MemoryId, authorizedRequest.UserId, cancellationToken);
                // if (!isOwner) throw new UnauthorizedAccessException();
            }

            return await next(cancellationToken);
        }
    }
}
