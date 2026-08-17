using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Business.Projectors
{
    /// <summary>
    /// Projects domain events into the SQL read model with idempotency tracking.
    /// Implements <see cref="IMemoryEventHandler"/> for single- and batch-event projection.
    /// </summary>
    public class MemoryEventProjector : IMemoryEventHandler
    {
        private readonly IReadModelRepository _readRepo;
        private readonly IEventIdempotencyStore _idempotencyStore;

        public MemoryEventProjector(IReadModelRepository readRepo, IEventIdempotencyStore idempotencyStore)
        {
            _readRepo = readRepo;
            _idempotencyStore = idempotencyStore;
        }

        // ─── IMemoryEventHandler ─────────────────────────────────────────────────

        public async Task HandleAsync(MemoryEvent evt, CancellationToken ct)
            => await ApplyEventIfNotProcessedAsync(evt, ct);

        public Task HandleAsync(IEnumerable<MemoryEvent> events, CancellationToken ct)
            => Handle(events, ct);

        // ─── Typed handlers ──────────────────────────────────────────────────────

        public async Task Handle(MemoryAddedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _idempotencyStore.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            var summary = evt.RawText?.Length > 300 ? evt.RawText[..300] + "..." : evt.RawText ?? string.Empty;
            var model = new MemoryReadModel
            {
                MemoryId = evt.AggregateId,
                UserId = evt.UserId ?? string.Empty,
                Summary = summary,
                TokenCount = CountTokens(summary),
                Archived = false,
                Importance = Enum.TryParse<MemoryImportance>(evt.Importance, ignoreCase: true, out var imp) ? imp : MemoryImportance.Medium,
                CreatedAt = evt.Timestamp
            };

            await _readRepo.UpsertAsync(model, ct);
            await _idempotencyStore.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(MemoryUpdatedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _idempotencyStore.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            if (evt.UpdatedFields != null && evt.UpdatedFields.TryGetValue("RawText", out var newText))
            {
                var summary = newText.Length > 300 ? newText[..300] + "..." : newText;
                var model = new MemoryReadModel
                {
                    MemoryId = evt.AggregateId,
                    UserId = evt.UserId ?? string.Empty,
                    Summary = summary,
                    TokenCount = CountTokens(summary),
                    Archived = false,
                    CreatedAt = evt.Timestamp
                };
                await _readRepo.UpsertAsync(model, ct);
            }

            await _idempotencyStore.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(MemoryCompressedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _idempotencyStore.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            var summary = evt.CompressedText ?? string.Empty;
            var model = new MemoryReadModel
            {
                MemoryId = evt.AggregateId,
                UserId = evt.UserId ?? string.Empty,
                Summary = summary,
                TokenCount = CountTokens(summary),
                Archived = false,
                CreatedAt = evt.Timestamp
            };

            await _readRepo.UpsertAsync(model, ct);
            await _idempotencyStore.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(MemoryConsolidatedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _idempotencyStore.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            var summary = evt.ConsolidatedText ?? string.Empty;
            var model = new MemoryReadModel
            {
                MemoryId = evt.AggregateId,
                UserId = evt.UserId ?? string.Empty,
                Summary = summary,
                TokenCount = CountTokens(summary),
                Archived = false,
                CreatedAt = evt.Timestamp
            };

            await _readRepo.UpsertAsync(model, ct);
            await _idempotencyStore.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(MemoryIndexedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _idempotencyStore.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            await _idempotencyStore.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(MemoryDeletedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _idempotencyStore.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            var model = new MemoryReadModel
            {
                MemoryId = evt.AggregateId,
                UserId = evt.UserId ?? string.Empty,
                Summary = string.Empty,
                TokenCount = 0,
                Archived = true,
                CreatedAt = evt.Timestamp
            };

            await _readRepo.UpsertAsync(model, ct);
            await _idempotencyStore.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(MemoryArchivedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _idempotencyStore.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            var model = new MemoryReadModel
            {
                MemoryId = evt.AggregateId,
                UserId = evt.UserId ?? string.Empty,
                Archived = true,
                CreatedAt = evt.Timestamp
            };

            await _readRepo.UpsertAsync(model, ct);
            await _idempotencyStore.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        public async Task Handle(SnapshotCreatedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;
            if (await _idempotencyStore.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

            await _idempotencyStore.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
        }

        // ─── Batch handler ───────────────────────────────────────────────────────

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
            if (await _idempotencyStore.HasProcessedAsync(evt.AggregateId, evt.Version, ct)) return;

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
                    await _idempotencyStore.MarkProcessedAsync(evt.AggregateId, evt.Version, ct);
                    break;
            }
        }

        private static int CountTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}
