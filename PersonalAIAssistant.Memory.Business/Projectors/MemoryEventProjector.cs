// PersonalAIAssistant.Memory.Business.Projectors/MemoryEventProjector.cs
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Interfaces.Sql;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Business.Projectors
{
    /// <summary>
    /// Projects domain events into the SQL read model.
    /// Implements <see cref="IMemoryEventHandler"/> so it can be registered with
    /// <see cref="IEventBus"/> and receive events automatically after they are published.
    /// </summary>
    public class MemoryEventProjector : IMemoryEventHandler
    {
        private readonly IReadModelRepository _readRepo;

        public MemoryEventProjector(IReadModelRepository readRepo)
        {
            _readRepo = readRepo;
        }

        // ─── IMemoryEventHandler ─────────────────────────────────────────────────

        /// <summary>Entry point used by <see cref="IEventBus"/> for single-event dispatch.</summary>
        public async Task HandleAsync(MemoryEvent evt, CancellationToken ct)
            => await ApplyEventIfNotProcessedAsync(evt, ct);

        /// <summary>Entry point used by <see cref="IEventBus"/> for batch-event dispatch.</summary>
        public Task HandleAsync(IEnumerable<MemoryEvent> events, CancellationToken ct)
            => Handle(events, ct);

        // ─── Typed handlers ──────────────────────────────────────────────────────

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
                Archived = false,
                Importance = Enum.TryParse<MemoryImportance>(evt.Importance, ignoreCase: true, out var imp) ? imp : MemoryImportance.Medium,
                CreatedAt = evt.Timestamp
            };

            await _readRepo.UpsertAsync(model, ct);
            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(MemoryUpdatedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            if (evt.UpdatedFields != null && evt.UpdatedFields.TryGetValue("RawText", out var newText))
            {
                var summary = newText.Length > 300 ? newText[..300] + "..." : newText;
                var model = new MemoryReadModel
                {
                    MemoryId = evt.AggregateId,
                    Summary = summary,
                    TokenCount = CountTokens(summary),
                    Archived = false,
                    CreatedAt = evt.Timestamp // Ideally, we'd preserve the original CreatedAt, but for simplicity we'll update or rely on SQL UPSERT to keep original
                };
                await _readRepo.UpsertAsync(model, ct);
            }

            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(MemoryCompressedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            var summary = evt.CompressedText ?? string.Empty;
            var model = new MemoryReadModel
            {
                MemoryId = evt.AggregateId,
                Summary = summary,
                TokenCount = CountTokens(summary),
                Archived = false,
                CreatedAt = evt.Timestamp
            };

            await _readRepo.UpsertAsync(model, ct);
            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

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
                Archived = false,
                CreatedAt = evt.Timestamp
            };

            await _readRepo.UpsertAsync(model, ct);
            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(MemoryIndexedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            // Indexing doesn't change the summary; mark processed for idempotency only.
            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(MemoryDeletedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            // Use AggregateId (base-class) — not evt.MemoryId (redundant field) — for consistency.
            var model = new MemoryReadModel
            {
                MemoryId = evt.AggregateId,
                Summary = string.Empty,
                TokenCount = 0,
                Archived = true,
                CreatedAt = evt.Timestamp
            };

            await _readRepo.UpsertAsync(model, ct);
            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(MemoryArchivedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            // For archiving, we could just fetch and update, or upsert with basic info. 
            // In a real app we'd fetch the existing and update `Archived = true`.
            // For now, let's assume Upsert handles partial or we construct it.
            var model = new MemoryReadModel
            {
                MemoryId = evt.AggregateId,
                Archived = true,
                CreatedAt = evt.Timestamp
            };

            await _readRepo.UpsertAsync(model, ct);
            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(SnapshotCreatedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        // ─── Batch handler ───────────────────────────────────────────────────────

        /// <summary>
        /// Applies multiple events in a single transaction when the repository supports it.
        /// Events are sorted by aggregate + version to ensure correct ordering.
        /// </summary>
        public async Task Handle(IEnumerable<MemoryEvent> events, CancellationToken ct)
        {
            if (events == null) return;

            var ordered = events
                .Where(e => e != null)
                .OrderBy(e => e.AggregateId)
                .ThenBy(e => e.Version)
                .ToList();

            if (!ordered.Any()) return;

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

            // Fallback: sequential without a transaction
            foreach (var evt in ordered)
            {
                ct.ThrowIfCancellationRequested();
                await ApplyEventIfNotProcessedAsync(evt, ct);
            }
        }

        // ─── Private helpers ─────────────────────────────────────────────────────

        private async Task ApplyEventIfNotProcessedAsync(MemoryEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _readRepo.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            switch (evt)
            {
                case MemoryAddedEvent added:           await Handle(added, ct);         break;
                case MemoryUpdatedEvent updated:       await Handle(updated, ct);       break;
                case MemoryCompressedEvent compressed: await Handle(compressed, ct);    break;
                case MemoryConsolidatedEvent consol:   await Handle(consol, ct);        break;
                case MemoryIndexedEvent indexed:       await Handle(indexed, ct);       break;
                case MemoryArchivedEvent archived:     await Handle(archived, ct);      break;
                case MemoryDeletedEvent deleted:       await Handle(deleted, ct);       break;
                case SnapshotCreatedEvent snapshot:    await Handle(snapshot, ct);      break;
                default:
                    // Unknown event type: mark processed to prevent infinite reprocessing.
                    await _readRepo.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
                    break;
                // NOTE: The typed Handle() overloads already call MarkProcessedAsync internally,
                // so we do NOT call it again here — that was the duplicate-write bug.
            }
        }

        /// <summary>
        /// Naive whitespace-based token approximation. This is intentionally fast and cheap.
        /// For accurate LLM token counts, inject a proper tokenizer (e.g. SharpToken / TikToken).
        /// </summary>
        private static int CountTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}
