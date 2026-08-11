using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Core.Interfaces.Sql;
using PersonalAIAssistant.Memory.Core.Interfaces.Mongo;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Business.Workers
{
    public class RetentionWorker : BackgroundService
    {
        private readonly ILogger<RetentionWorker> _logger;
        private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;
        private readonly RetentionOptions _opts;

        public RetentionWorker(
            ILogger<RetentionWorker> logger,
            Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory,
            IOptions<RetentionOptions> opts)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _opts = opts.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RetentionWorker started.");

            // Run once an hour
            var delay = TimeSpan.FromHours(1);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var readRepo = scope.ServiceProvider.GetRequiredService<IReadModelRepository>();
                        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
                        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

                        await ProcessExpiredMemoriesAsync(readRepo, eventStore, eventBus, stoppingToken);
                        
                        if (_opts.HardDeleteEnabled)
                        {
                            await ProcessArchivedMemoriesAsync(readRepo, eventStore, eventBus, stoppingToken);
                        }

                        await EnforceCapacityLimitAsync(readRepo, "default", stoppingToken);
                    }

                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in RetentionWorker. Backing off.");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }

            _logger.LogInformation("RetentionWorker stopping.");
        }

        private async Task ProcessExpiredMemoriesAsync(IReadModelRepository readRepo, IEventStore eventStore, IEventBus eventBus, CancellationToken ct)
        {
            var expired = await readRepo.GetExpiredMemoriesAsync(_opts.TtlDays, ct);
            foreach (var candidate in expired)
            {
                await ArchiveMemoryAsync(readRepo, eventStore, eventBus, candidate, "TTL expired", ct);
            }
        }

        private async Task ProcessArchivedMemoriesAsync(IReadModelRepository readRepo, IEventStore eventStore, IEventBus eventBus, CancellationToken ct)
        {
            var archived = await readRepo.GetArchivedMemoriesAsync(_opts.ArchiveDays, ct);
            foreach (var candidate in archived)
            {
                await DeleteMemoryAsync(readRepo, eventStore, eventBus, candidate, "Hard delete after archive period", ct);
            }
        }

        private async Task EnforceCapacityLimitAsync(IReadModelRepository readRepo, string userId, CancellationToken ct)
        {
            var count = await readRepo.GetMemoryCountByUserAsync(userId, ct);
            if (count > _opts.MaxMemoriesPerUser)
            {
                _logger.LogWarning("User {UserId} exceeded max memories ({Count} > {Max}). Archiving oldest.", 
                    userId, count, _opts.MaxMemoriesPerUser);
            }
        }

        private async Task ArchiveMemoryAsync(IReadModelRepository readRepo, IEventStore eventStore, IEventBus eventBus, ReadModelCandidate candidate, string reason, CancellationToken ct)
        {
            if (!await readRepo.TryMarkProcessingAsync(candidate.MemoryId, ct)) return;

            try
            {
                var history = await eventStore.GetEventsAsync(candidate.StreamId, ct);
                var aggregate = new MemoryAggregate();
                aggregate.LoadFromHistory(history);

                aggregate.Archive(reason, "system");

                var newEvents = aggregate.UncommittedEvents.ToList();
                if (newEvents.Any())
                {
                    var expectedVersion = aggregate.Version - newEvents.Count;
                    await eventStore.AppendEventsAsync(candidate.StreamId, newEvents, expectedVersion, ct);
                    await eventBus.PublishAsync(newEvents, ct);
                }
                
                await readRepo.MarkProcessedAsync(candidate.MemoryId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to archive memory {MemoryId}", candidate.MemoryId);
                await readRepo.UnmarkProcessingAsync(candidate.MemoryId, ct);
            }
        }

        private async Task DeleteMemoryAsync(IReadModelRepository readRepo, IEventStore eventStore, IEventBus eventBus, ReadModelCandidate candidate, string reason, CancellationToken ct)
        {
            if (!await readRepo.TryMarkProcessingAsync(candidate.MemoryId, ct)) return;

            try
            {
                var history = await eventStore.GetEventsAsync(candidate.StreamId, ct);
                var aggregate = new MemoryAggregate();
                aggregate.LoadFromHistory(history);

                aggregate.Delete(reason, "system");

                var newEvents = aggregate.UncommittedEvents.ToList();
                if (newEvents.Any())
                {
                    var expectedVersion = aggregate.Version - newEvents.Count;
                    await eventStore.AppendEventsAsync(candidate.StreamId, newEvents, expectedVersion, ct);
                    await eventBus.PublishAsync(newEvents, ct);
                }
                
                await readRepo.MarkProcessedAsync(candidate.MemoryId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete memory {MemoryId}", candidate.MemoryId);
                await readRepo.UnmarkProcessingAsync(candidate.MemoryId, ct);
            }
        }
    }
}
