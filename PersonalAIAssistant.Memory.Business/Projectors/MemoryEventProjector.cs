// PersonalAIAssistant.Memory.Business.Projectors/MemoryEventProjector.cs
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Events;
using PersonalAIAssistant.Memory.Infrastructure.Sql;

namespace PersonalAIAssistant.Memory.Business.Projectors
{
    public class MemoryEventProjector
    {
        private readonly IReadModelRepository _readRepo;

        public MemoryEventProjector(IReadModelRepository readRepo)
        {
            _readRepo = readRepo;
        }

        // Existing single-event handlers (unchanged)
        // MemoryAddedEvent
        public async Task Handle(MemoryAddedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            var summary = evt.RawText?.Length > 300 ? evt.RawText[..300] + "..." : evt.RawText ?? string.Empty;
            var model = new MemoryReadModel
            {
                MemoryId = evt.AggregateId,
                Summary = summary,
                TokenCount = CountTokens(summary),
                Archived = false
            };

            await _readRepo.UpsertAsync(model, ct);
            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        // MemoryUpdatedEvent
        public async Task Handle(MemoryUpdatedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            // If RawText was updated, update summary/token count
            if (evt.UpdatedFields != null && evt.UpdatedFields.TryGetValue(nameof(MemoryReadModel.Summary), out var newSummary))
            {
                var model = new MemoryReadModel
                {
                    MemoryId = evt.AggregateId,
                    Summary = newSummary,
                    TokenCount = CountTokens(newSummary),
                    Archived = false
                };
                await _readRepo.UpsertAsync(model, ct);
            }
            else if (evt.UpdatedFields != null && evt.UpdatedFields.TryGetValue(nameof(MemoryReadModel.Summary), out _))
            {
                // handled above
            }
            else
            {
                // For other updates, we may choose to refresh read model by rehydrating or ignore.
            }

            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        // MemoryCompressedEvent
        public async Task Handle(MemoryCompressedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            // Update read model summary with compressed text (shorter representation)
            var summary = evt.CompressedText ?? string.Empty;
            var model = new MemoryReadModel
            {
                MemoryId = evt.AggregateId,
                Summary = summary,
                TokenCount = CountTokens(summary),
                Archived = false
            };

            await _readRepo.UpsertAsync(model, ct);
            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        // MemoryConsolidatedEvent
        public async Task Handle(MemoryConsolidatedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            var summary = evt.ConsolidatedText ?? string.Empty;
            var model = new MemoryReadModel
            {
                MemoryId = evt.AggregateId,
                Summary = summary,
                TokenCount = CountTokens(summary),
                Archived = false
            };

            await _readRepo.UpsertAsync(model, ct);
            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        // MemoryIndexedEvent (embedding/semantic index)
        public async Task Handle(MemoryIndexedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            // Indexing doesn't necessarily change summary; we still mark processed.
            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        // MemoryDeletedEvent
        public async Task Handle(MemoryDeletedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            var model = new MemoryReadModel
            {
                MemoryId = evt.MemoryId,
                Summary = string.Empty,
                TokenCount = 0,
                Archived = true
            };

            await _readRepo.UpsertAsync(model, ct);
            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        // SnapshotCreatedEvent (optional: update last processed marker)
        public async Task Handle(SnapshotCreatedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            // Snapshot event does not change read model content by default, but mark processed for idempotency.
            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        // Batched handler: applies multiple events in a single transaction when supported
        public async Task Handle(IEnumerable<MemoryEvent> events, CancellationToken ct)
        {
            if (events == null) return;

            // Normalize and order events by aggregate and version
            var ordered = events
                .Where(e => e != null)
                .OrderBy(e => e.AggregateId)
                .ThenBy(e => e.Version)
                .ToList();

            if (!ordered.Any()) return;

            // If repository supports transactions, run the whole batch in one transaction
            if (_readRepo is ITransactionalReadModelRepository transactional)
            {
                await transactional.ExecuteInTransactionAsync(async token =>
                {
                    foreach (var evt in ordered)
                    {
                        token.ThrowIfCancellationRequested();
                        await ApplyEventIfNotProcessedAsync(evt, token);
                    }
                }, ct);

                return;
            }

            // Fallback: process sequentially without a transaction
            foreach (var evt in ordered)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await ApplyEventIfNotProcessedAsync(evt, ct);
                }
                catch (Exception)
                {
                    // Log and continue (or rethrow depending on your policy) or
                    // For now, rethrow to surface the failure to the caller
                    throw;
                }
            }
        }

        // Helper: check idempotency and dispatch to concrete handlers
        private async Task ApplyEventIfNotProcessedAsync(MemoryEvent evt, CancellationToken ct)
        {
            if (evt == null) return;

            var aggregateId = evt.AggregateId;
            if (await _readRepo.HasProcessedAsync(aggregateId, evt.Version, ct)) return;

            switch (evt)
            {
                case MemoryAddedEvent added:
                    await Handle(added, ct);
                    break;
                case MemoryUpdatedEvent updated:
                    await Handle(updated, ct);
                    break;
                case MemoryCompressedEvent compressed:
                    await Handle(compressed, ct);
                    break;
                case MemoryConsolidatedEvent consolidated:
                    await Handle(consolidated, ct);
                    break;
                case MemoryIndexedEvent indexed:
                    await Handle(indexed, ct);
                    break;
                case MemoryDeletedEvent deleted:
                    await Handle(deleted, ct);
                    break;
                case SnapshotCreatedEvent snapshot:
                    await Handle(snapshot, ct);
                    break;
                default:
                    // Unknown event type: mark processed to avoid reprocessing, or log and skip
                    await _readRepo.MarkProcessedAsync(aggregateId, evt.Version, ct);
                    break;
            }

            // Ensure we mark the event as processed (idempotency)
            await _readRepo.MarkProcessedAsync(aggregateId, evt.Version, ct);
        }

        // Helper: naive token count (if used by single-event handlers)
        private static int CountTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}
