using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Interfaces;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;

namespace PersonalAIAssistant.Memory.Business.Workers
{
    public class SnapshotWorkerOptions
    {
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(5);
        public int SnapshotEventThreshold { get; set; } = 100;
        public int BatchSize { get; set; } = 50;
    }

    public class SnapshotWorker : BackgroundService
    {
        private readonly ILogger<SnapshotWorker> _logger;
        private readonly IEventStore _eventStore;
        private readonly ISnapshotRepository _snapshotRepo;
        private readonly IOptions<SnapshotWorkerOptions> _opts;

        public SnapshotWorker(
            ILogger<SnapshotWorker> logger,
            IEventStore eventStore,
            ISnapshotRepository snapshotRepo,
            IOptions<SnapshotWorkerOptions> opts)
        {
            _logger = logger;
            _eventStore = eventStore;
            _snapshotRepo = snapshotRepo;
            _opts = opts;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SnapshotWorker started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var candidates = await _snapshotRepo.GetStreamsNeedingSnapshotAsync(
                        _opts.Value.SnapshotEventThreshold, _opts.Value.BatchSize, stoppingToken);

                    foreach (var streamId in candidates)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        try
                        {
                            var snapshot = await _snapshotRepo.GetLatestSnapshotAsync(streamId, stoppingToken);
                            var events = await _eventStore.GetEventsAsync(streamId, stoppingToken);

                            // Rehydrate aggregate using snapshot if present
                            var aggregate = new MemoryAggregate();
                            if (snapshot != null)
                            {
                                // Snapshot payload deserialization is infra-specific; assume snapshot payload is JSON of helper model
                                aggregate = MemoryAggregateFactory.RehydrateFromSnapshot(snapshot, events);
                            }
                            else
                            {
                                aggregate.LoadFromHistory(events);
                            }

                            var payload = MemoryAggregateFactory.CreateSnapshotPayload(aggregate);
                            await _snapshotRepo.SaveSnapshotAsync(streamId, payload, aggregate.Version, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to snapshot stream {StreamId}", streamId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SnapshotWorker top-level error");
                }

                await Task.Delay(_opts.Value.PollInterval, stoppingToken);
            }
            _logger.LogInformation("SnapshotWorker stopping.");
        }
    }
}

