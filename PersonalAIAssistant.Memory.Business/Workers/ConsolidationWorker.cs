using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
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
        private readonly IReadModelRepository _readRepo;
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;
        private readonly ICompressionService _compressionService;
        private readonly ConsolidationWorkerOptions _opts;
        private readonly SemaphoreSlim _llmSemaphore;

        public ConsolidationWorker(
            ILogger<ConsolidationWorker> logger,
            IReadModelRepository readRepo,
            IEventStore eventStore,
            IEventBus eventBus,
            ICompressionService compressionService,
            IOptions<ConsolidationWorkerOptions> opts)
        {
            _logger = logger;
            _readRepo = readRepo;
            _eventStore = eventStore;
            _eventBus = eventBus;
            _compressionService = compressionService;
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
                    var candidates = await _readRepo.GetConsolidationCandidatesAsync(_opts.BatchSize, stoppingToken);

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
            // Idempotency: skip if another worker instance already claimed this item.
            if (!await _readRepo.TryMarkProcessingAsync(candidate.MemoryId, ct))
                return;

            // Acquire semaphore AFTER claiming the lock so we don't block the lock path.
            await _llmSemaphore.WaitAsync(ct);
            try
            {
                // 1. Compress/summarize text via LLM
                var compressed = await _compressionService.CompressAsync(candidate.Text, ct);

                // 2. Load aggregate history and apply domain logic
                var history = await _eventStore.GetEventsAsync(candidate.StreamId, ct);
                var aggregate = new MemoryAggregate();
                aggregate.LoadFromHistory(history);

                aggregate.Compress(compressed.Text, compressed.Model, compressed.TokenCount, userId: "system");

                // 3. Persist and publish
                var newEvents = aggregate.UncommittedEvents.ToList();
                var expectedVersion = aggregate.Version - newEvents.Count;

                await _eventStore.AppendEventsAsync(candidate.StreamId, newEvents, expectedVersion, ct);
                await _eventBus.PublishAsync(newEvents, ct);

                // 4. Mark as done
                await _readRepo.MarkProcessedAsync(candidate.MemoryId, ct);

                aggregate.ClearUncommittedEvents();
            }
            catch (ConcurrencyException)
            {
                _logger.LogWarning("Concurrency conflict while consolidating {MemoryId} — will retry next cycle.", candidate.MemoryId);
                await _readRepo.UnmarkProcessingAsync(candidate.MemoryId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consolidating {MemoryId}.", candidate.MemoryId);
                await _readRepo.UnmarkProcessingAsync(candidate.MemoryId, ct);
            }
            finally
            {
                _llmSemaphore.Release();
            }
        }
    }
}
