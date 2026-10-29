using System.Collections.Concurrent;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Security;

public class DefaultExternalAccessClient : IExternalAccessClient
{
    private readonly ConcurrentDictionary<string, ExternalGrantResult> _remoteGrants = new();

    public Task<ExternalGrantResult> GrantAccessAsync(
        string userId, 
        string targetResourceId, 
        string permissionLevel, 
        string idempotencyKey, 
        CancellationToken ct = default)
    {
        // Check for existing grant with this idempotency key
        if (_remoteGrants.TryGetValue(idempotencyKey, out var existing))
        {
            return Task.FromResult(existing);
        }

        var grant = new ExternalGrantResult($"ext-grant-{Guid.NewGuid():N}", true);
        _remoteGrants[idempotencyKey] = grant;
        return Task.FromResult(grant);
    }

    public Task<ExternalGrantResult?> QueryGrantByIdempotencyKeyAsync(
        string idempotencyKey, 
        CancellationToken ct = default)
    {
        if (_remoteGrants.TryGetValue(idempotencyKey, out var grant))
        {
            return Task.FromResult<ExternalGrantResult?>(grant);
        }

        return Task.FromResult<ExternalGrantResult?>(null);
    }
}
