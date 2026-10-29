using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Security;

public record AccessAssignmentPayload(
    Guid AssignmentId, 
    string UserId, 
    string TargetResourceId, 
    string PermissionLevel, 
    string IdempotencyKey);

public class AccessAssignmentConsumer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IExternalAccessClient _externalClient;
    private readonly ILogger<AccessAssignmentConsumer> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    public AccessAssignmentConsumer(
        IServiceScopeFactory scopeFactory,
        IExternalAccessClient externalClient,
        ILogger<AccessAssignmentConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _externalClient = externalClient;
        _logger = logger;

        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(500),
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
            })
            .Build();
    }

    public async Task ProcessAssignmentAsync(string payloadJson, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Deserialize<AccessAssignmentPayload>(payloadJson);
        if (payload == null)
        {
            _logger.LogWarning("Failed to deserialize access assignment event payload.");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAccessAssignmentRepository>();

        var assignment = await repository.GetByIdAsync(payload.AssignmentId, ct);
        if (assignment == null)
        {
            _logger.LogWarning("Access assignment {AssignmentId} not found in database.", payload.AssignmentId);
            return;
        }

        // Idempotency check: if already active, do not grant duplicate access
        if (assignment.Status == AssignmentStatus.Active)
        {
            _logger.LogInformation("Access assignment {AssignmentId} is already Active (Idempotent bypass).", assignment.Id);
            return;
        }

        assignment.Status = AssignmentStatus.Provisioning;
        await repository.UpdateAsync(assignment, ct);

        try
        {
            var result = await _retryPipeline.ExecuteAsync(async token =>
                await _externalClient.GrantAccessAsync(
                    payload.UserId,
                    payload.TargetResourceId,
                    payload.PermissionLevel,
                    payload.IdempotencyKey,
                    token), ct);

            assignment.Status = AssignmentStatus.Active;
            assignment.ProvisionedAtUtc = DateTime.UtcNow;
            assignment.ExternalReferenceId = result.GrantId;
            assignment.LastError = null;
            await repository.UpdateAsync(assignment, ct);
            _logger.LogInformation("Successfully granted access for assignment {AssignmentId}", assignment.Id);
        }
        catch (TimeoutException tex)
        {
            _logger.LogWarning(tex, "Timeout calling external access service for assignment {AssignmentId}. Status is UNKNOWN.", assignment.Id);
            await HandleIndeterminateStateAsync(assignment, repository, ct);
        }
        catch (HttpRequestException hex) when (hex.StatusCode == HttpStatusCode.GatewayTimeout || hex.StatusCode == HttpStatusCode.RequestTimeout)
        {
            _logger.LogWarning(hex, "HTTP Gateway Timeout for assignment {AssignmentId}. Checking external status.", assignment.Id);
            await HandleIndeterminateStateAsync(assignment, repository, ct);
        }
        catch (Exception ex)
        {
            assignment.RetryCount++;
            assignment.LastError = ex.Message;

            if (assignment.RetryCount >= 5)
            {
                // Route to Dead-Letter state for investigation and controlled replay
                assignment.Status = AssignmentStatus.Failed;
                await repository.UpdateAsync(assignment, ct);
                _logger.LogCritical(ex, "Access assignment {AssignmentId} failed after {Retries} attempts. Routed to Dead-Letter Queue.", assignment.Id, assignment.RetryCount);
            }
            else
            {
                await repository.UpdateAsync(assignment, ct);
                throw;
            }
        }
    }

    private async Task HandleIndeterminateStateAsync(
        AccessAssignmentEntity assignment, 
        IAccessAssignmentRepository repository, 
        CancellationToken ct)
    {
        // Query external service using idempotency key to reconcile unknown state
        var remoteStatus = await _externalClient.QueryGrantByIdempotencyKeyAsync(assignment.IdempotencyKey, ct);
        if (remoteStatus is { IsActive: true })
        {
            assignment.Status = AssignmentStatus.Active;
            assignment.ProvisionedAtUtc = DateTime.UtcNow;
            assignment.ExternalReferenceId = remoteStatus.GrantId;
            assignment.LastError = null;
            _logger.LogInformation("Reconciliation verified remote grant for assignment {AssignmentId}", assignment.Id);
        }
        else
        {
            // Indeterminate state: mark as requiring background reconciliation sweep
            assignment.Status = AssignmentStatus.RequiresReconciliation;
            assignment.LastError = "Remote call timed out; grant state unverified.";
            _logger.LogWarning("Marked assignment {AssignmentId} as RequiresReconciliation.", assignment.Id);
        }

        await repository.UpdateAsync(assignment, ct);
    }
}
