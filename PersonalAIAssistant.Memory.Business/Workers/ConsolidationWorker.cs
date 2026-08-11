using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using Polly.Registry;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Core.Interfaces.Mongo;
using PersonalAIAssistant.Memory.Core.Interfaces.Sql;

namespace PersonalAIAssistant.Memory.Business.Workers
{
    /// <summary>Configuration options for <see cref="ConsolidationWorker"/>.</summary>
    public class ConsolidationWorkerOptions
    {
        /// <summary>How long to wait between consolidation batches when the queue is empty.</summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Maximum number of memories processed per batch.</summary>
        public int BatchSize { get; set; } = 10;

        /// <summary>Maximum number of concurrent LLM compression calls.</summary>
        public int MaxConcurrentLLM { get; set; } = 3;
    }

    public class ConsolidationWorker : BackgroundService
    {
        private readonly ILogger<ConsolidationWorker> _logger;
        private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;
        private readonly ResiliencePipelineProvider<string> _resilienceProvider;
        private readonly ConsolidationWorkerOptions _opts;
        private readonly SemaphoreSlim _llmSemaphore;

        public ConsolidationWorker(
            ILogger<ConsolidationWorker> logger,
            Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory,
            ResiliencePipelineProvider<string> resilienceProvider,
            IOptions<ConsolidationWorkerOptions> opts)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _resilienceProvider = resilienceProvider;
            _opts = opts.Value;
            _llmSemaphore = new SemaphoreSlim(_opts.MaxConcurrentLLM);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ConsolidationWorker started (BatchSize={BatchSize}, MaxConcurrentLLM={MaxConcurrentLLM}).",
                _opts.BatchSize, _opts.MaxConcurrentLLM);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var readRepo = scope.ServiceProvider.GetRequiredService<IReadModelRepository>();
                    var candidates = await readRepo.GetConsolidationCandidatesAsync(_opts.BatchSize, stoppingToken);

                    if (!candidates.Any())
                    {
                        await Task.Delay(_opts.PollInterval, stoppingToken);
                        continue;
                    }

                    var tasks = candidates.Select(c => ProcessCandidateAsync(c, stoppingToken));
                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;  // graceful shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Top-level error in ConsolidationWorker — backing off.");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }

            _logger.LogInformation("ConsolidationWorker stopping.");
        }

        private async Task ProcessCandidateAsync(ReadModelCandidate candidate, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var readRepo = scope.ServiceProvider.GetRequiredService<IReadModelRepository>();
            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
            var compressionService = scope.ServiceProvider.GetRequiredService<ICompressionService>();

            // Idempotency: skip if another worker instance already claimed this item.
            if (!await readRepo.TryMarkProcessingAsync(candidate.MemoryId, ct))
                return;

            // Acquire semaphore AFTER claiming the lock so we don't block the lock path.
            await _llmSemaphore.WaitAsync(ct);
            try
            {
                var aiPipeline = _resilienceProvider.GetPipeline("AiServiceProtection");
                var workerPipeline = _resilienceProvider.GetPipeline("WorkerRetry");

                // 1. Compress/summarize text via LLM (protected by Circuit Breaker & Retry)
                var compressed = await aiPipeline.ExecuteAsync(async token => 
                    await compressionService.CompressAsync(candidate.Text, token), ct);

                // 2. Load aggregate history and apply domain logic
                var history = await workerPipeline.ExecuteAsync(async token => 
                    await eventStore.GetEventsAsync(candidate.StreamId, token), ct);
                
                var aggregate = new MemoryAggregate();
                aggregate.LoadFromHistory(history);

                aggregate.Compress(compressed.Text, compressed.Model, compressed.TokenCount, userId: "system");

                // 3. Persist and publish
                var newEvents = aggregate.UncommittedEvents.ToList();
                var expectedVersion = aggregate.Version - newEvents.Count;

                await workerPipeline.ExecuteAsync(async token => 
                {
                    await eventStore.AppendEventsAsync(candidate.StreamId, newEvents, expectedVersion, token);
                    await eventBus.PublishAsync(newEvents, token);
                    await readRepo.MarkProcessedAsync(candidate.MemoryId, token);
                }, ct);

                aggregate.ClearUncommittedEvents();
            }
            catch (ConcurrencyException)
            {
                _logger.LogWarning("Concurrency conflict while consolidating {MemoryId} — will retry next cycle.", candidate.MemoryId);
                await readRepo.UnmarkProcessingAsync(candidate.MemoryId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consolidating {MemoryId}.", candidate.MemoryId);
                await readRepo.UnmarkProcessingAsync(candidate.MemoryId, ct);
            }
            finally
            {
                _llmSemaphore.Release();
            }
        }
    }
}
