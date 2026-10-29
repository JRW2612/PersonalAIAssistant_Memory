using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Security;

public class ReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IExternalAccessClient _externalClient;
    private readonly ILogger<ReconciliationWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _staleAge = TimeSpan.FromMinutes(5);

    public ReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        IExternalAccessClient externalClient,
        ILogger<ReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _externalClient = externalClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunReconciliationSweepAsync(stoppingToken);
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during reconciliation sweep.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    public async Task RunReconciliationSweepAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAccessAssignmentRepository>();

        var inDoubtRecords = await repository.GetStaleOrInDoubtAssignmentsAsync(_staleAge, 50, ct);
        if (!inDoubtRecords.Any()) return;

        _logger.LogInformation("Reconciling {Count} in-doubt access assignments.", inDoubtRecords.Count);

        foreach (var assignment in inDoubtRecords)
        {
            try
            {
                var remoteStatus = await _externalClient.QueryGrantByIdempotencyKeyAsync(assignment.IdempotencyKey, ct);
                assignment.LastReconciledAtUtc = DateTime.UtcNow;

                if (remoteStatus is { IsActive: true })
                {
                    assignment.Status = AssignmentStatus.Active;
                    assignment.ProvisionedAtUtc = DateTime.UtcNow;
                    assignment.ExternalReferenceId = remoteStatus.GrantId;
                    assignment.LastError = null;
                    _logger.LogInformation("Reconciliation: Healed assignment {AssignmentId} to Active.", assignment.Id);
                }
                else
                {
                    assignment.Status = AssignmentStatus.Failed;
                    assignment.LastError = "Reconciliation: External service confirms grant was never completed.";
                    _logger.LogWarning("Reconciliation: Marked assignment {AssignmentId} as Failed.", assignment.Id);
                }

                await repository.UpdateAsync(assignment, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation failed for assignment {AssignmentId}", assignment.Id);
            }
        }
    }
}
