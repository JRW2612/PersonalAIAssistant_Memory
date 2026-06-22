using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;
using PersonalAIAssistant.Memory.Infrastructure.Sql;
using System.Data;

namespace PersonalAIAssistant.Memory.Business.Workers
{
    public class ConsolidationWorker : BackgroundService
    {
        private readonly ILogger<ConsolidationWorker> _logger;
        private readonly IReadModelRepository _readRepo;
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;
        private readonly ICompressionService _compressionService;

        private readonly int _batchSize;
        private readonly SemaphoreSlim _llmSemaphore;

        public ConsolidationWorker(
            ILogger<ConsolidationWorker> logger,
            IReadModelRepository readRepo,
            IEventStore eventStore,
            IEventBus eventBus,
            ICompressionService compressionService,
            int batchSize = 10,
            int maxConcurrentLLM = 3)
        {
            _logger = logger;
            _readRepo = readRepo;
            _eventStore = eventStore;
            _eventBus = eventBus;
            _compressionService = compressionService;

            _batchSize = batchSize;
            _llmSemaphore = new SemaphoreSlim(maxConcurrentLLM);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ConsolidationWorker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 1. Get candidates for consolidation
                    var candidates = await _readRepo.GetConsolidationCandidatesAsync(_batchSize, stoppingToken);

                    if (!candidates.Any())
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                        continue;
                    }

                    // 2. Process candidates concurrently with bounded parallelism
                    var tasks = candidates.Select(c => ProcessCandidateAsync(c, stoppingToken));
                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Top-level error in ConsolidationWorker");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // backoff
                }
            }

            _logger.LogInformation("ConsolidationWorker stopping.");
        }

        private async Task ProcessCandidateAsync(ReadModelCandidate candidate, CancellationToken ct)
        {
            // Idempotency: mark candidate as "processing" to avoid duplicate work
            if (!await _readRepo.TryMarkProcessingAsync(candidate.MemoryId, ct))
                return;

            try
            {
                await _llmSemaphore.WaitAsync(ct);

                // 1. Compress/summarize text
                var compressed = await _compressionService.CompressAsync(candidate.Text, ct);

                // 2. Load aggregate history
                var history = await _eventStore.GetEventsAsync(candidate.StreamId, ct);
                var aggregate = new MemoryAggregate();
                aggregate.LoadFromHistory(history);

                // 3. Apply domain logic
                aggregate.Compress(compressed.Text, compressed.Model, compressed.TokenCount, userId: "system");

                // 4. Persist new events
                var newEvents = aggregate.UncommittedEvents.ToList();
                var expectedVersion = aggregate.Version - newEvents.Count;

                await _eventStore.AppendEventsAsync(candidate.StreamId, newEvents, expectedVersion, ct);

                // 5. Publish events
                foreach (var evt in newEvents)
                    await _eventBus.PublishAsync(evt, ct);

                // 6. Mark candidate as processed
                await _readRepo.MarkProcessedAsync(candidate.MemoryId, ct);

                aggregate.ClearUncommittedEvents();
            }
            catch (ConcurrencyException)
            {
                _logger.LogWarning("Concurrency conflict while consolidating {MemoryId}", candidate.MemoryId);
                await _readRepo.UnmarkProcessingAsync(candidate.MemoryId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consolidating {MemoryId}", candidate.MemoryId);
                await _readRepo.UnmarkProcessingAsync(candidate.MemoryId, ct);
            }
            finally
            {
                _llmSemaphore.Release();
            }
        }
    }
}
