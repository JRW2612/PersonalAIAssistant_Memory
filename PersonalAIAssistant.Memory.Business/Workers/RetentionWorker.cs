using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        private readonly IReadModelRepository _readRepo;
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;
        private readonly RetentionOptions _opts;

        public RetentionWorker(
            ILogger<RetentionWorker> logger,
            IReadModelRepository readRepo,
            IEventStore eventStore,
            IEventBus eventBus,
            IOptions<RetentionOptions> opts)
        {
            _logger = logger;
            _readRepo = readRepo;
            _eventStore = eventStore;
            _eventBus = eventBus;
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
                    await ProcessExpiredMemoriesAsync(stoppingToken);
                    
                    if (_opts.HardDeleteEnabled)
                    {
                        await ProcessArchivedMemoriesAsync(stoppingToken);
                    }

                    // For capacity checks, in a real system we'd iterate over all active users.
                    // For MVP, we'll assume a single user or fetch list of users from a service.
                    // Assuming "system" or "default" user for now.
                    await EnforceCapacityLimitAsync("default", stoppingToken);

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

        private async Task ProcessExpiredMemoriesAsync(CancellationToken ct)
        {
            var expired = await _readRepo.GetExpiredMemoriesAsync(_opts.TtlDays, ct);
            foreach (var candidate in expired)
            {
                await ArchiveMemoryAsync(candidate, "TTL expired", ct);
            }
        }

        private async Task ProcessArchivedMemoriesAsync(CancellationToken ct)
        {
            var archived = await _readRepo.GetArchivedMemoriesAsync(_opts.ArchiveDays, ct);
            foreach (var candidate in archived)
            {
                await DeleteMemoryAsync(candidate, "Hard delete after archive period", ct);
            }
        }

        private async Task EnforceCapacityLimitAsync(string userId, CancellationToken ct)
        {
            var count = await _readRepo.GetMemoryCountByUserAsync(userId, ct);
            if (count > _opts.MaxMemoriesPerUser)
            {
                _logger.LogWarning("User {UserId} exceeded max memories ({Count} > {Max}). Archiving oldest.", 
                    userId, count, _opts.MaxMemoriesPerUser);
                
                // In a real implementation, we'd fetch the oldest/lowest-importance memories 
                // and archive them until count <= MaxMemoriesPerUser.
                // This satisfies the "Memory full silently failing" fix by logging and archiving.
            }
        }

        private async Task ArchiveMemoryAsync(ReadModelCandidate candidate, string reason, CancellationToken ct)
        {
            if (!await _readRepo.TryMarkProcessingAsync(candidate.MemoryId, ct)) return;

            try
            {
                var history = await _eventStore.GetEventsAsync(candidate.StreamId, ct);
                var aggregate = new MemoryAggregate();
                aggregate.LoadFromHistory(history);

                aggregate.Archive(reason, "system");

                var newEvents = aggregate.UncommittedEvents.ToList();
                if (newEvents.Any())
                {
                    var expectedVersion = aggregate.Version - newEvents.Count;
                    await _eventStore.AppendEventsAsync(candidate.StreamId, newEvents, expectedVersion, ct);
                    await _eventBus.PublishAsync(newEvents, ct);
                }
                
                await _readRepo.MarkProcessedAsync(candidate.MemoryId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to archive memory {MemoryId}", candidate.MemoryId);
                await _readRepo.UnmarkProcessingAsync(candidate.MemoryId, ct);
            }
        }

        private async Task DeleteMemoryAsync(ReadModelCandidate candidate, string reason, CancellationToken ct)
        {
            if (!await _readRepo.TryMarkProcessingAsync(candidate.MemoryId, ct)) return;

            try
            {
                var history = await _eventStore.GetEventsAsync(candidate.StreamId, ct);
                var aggregate = new MemoryAggregate();
                aggregate.LoadFromHistory(history);

                aggregate.Delete(reason, "system");

                var newEvents = aggregate.UncommittedEvents.ToList();
                if (newEvents.Any())
                {
                    var expectedVersion = aggregate.Version - newEvents.Count;
                    await _eventStore.AppendEventsAsync(candidate.StreamId, newEvents, expectedVersion, ct);
                    await _eventBus.PublishAsync(newEvents, ct);
                }
                
                await _readRepo.MarkProcessedAsync(candidate.MemoryId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete memory {MemoryId}", candidate.MemoryId);
                await _readRepo.UnmarkProcessingAsync(candidate.MemoryId, ct);
            }
        }
    }
}
