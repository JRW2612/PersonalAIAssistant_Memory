using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using Polly.Registry;

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
        private readonly ResiliencePipelineProvider<string> _resilienceProvider;
        private readonly IOptions<SnapshotWorkerOptions> _opts;

        public SnapshotWorker(
            ILogger<SnapshotWorker> logger,
            IEventStore eventStore,
            ISnapshotRepository snapshotRepo,
            ResiliencePipelineProvider<string> resilienceProvider,
            IOptions<SnapshotWorkerOptions> opts)
        {
            _logger = logger;
            _eventStore = eventStore;
            _snapshotRepo = snapshotRepo;
            _resilienceProvider = resilienceProvider;
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
                            var workerPipeline = _resilienceProvider.GetPipeline("WorkerRetry");

                            await workerPipeline.ExecuteAsync(async token =>
                            {
                                var snapshot = await _snapshotRepo.GetLatestSnapshotAsync(streamId, token);
                                var fromVersion = snapshot?.Version ?? 0;
                                var tailEvents = snapshot != null
                                    ? await _eventStore.GetEventsFromVersionAsync(streamId, fromVersion, token)
                                    : await _eventStore.GetEventsAsync(streamId, token);

                                var aggregate = snapshot != null
                                    ? MemoryAggregateFactory.RehydrateFromSnapshot(snapshot, tailEvents)
                                    : MemoryAggregateFactory.RehydrateFromEvents(tailEvents);

                                var payload = MemoryAggregateFactory.CreateSnapshotPayload(aggregate);
                                await _snapshotRepo.SaveSnapshotAsync(streamId, payload, aggregate.Version, token);
                            }, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to snapshot stream {StreamId}", streamId);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SnapshotWorker top-level error");
                }

                try
                {
                    await Task.Delay(_opts.Value.PollInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            _logger.LogInformation("SnapshotWorker stopping.");
        }
    }
}
