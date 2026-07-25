// PersonalAIAssistant.Memory.Core/Domains/MemoryAggregate.cs
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.DTOs;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Utils;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Core.Domains
{
    public class MemoryAggregate
    {
        // Constructors
        public MemoryAggregate(MemoryId id) => Id = id;
        public MemoryAggregate() { }

        // State
        public MemoryId Id { get; private set; }
        public int Version { get; private set; }
        public MemoryStatus Status { get; private set; } = MemoryStatus.Active;
        public MemoryImportance Importance { get; private set; } = MemoryImportance.Medium;

        public string RawText { get; private set; } = string.Empty;
        public string? CompressedText { get; private set; }
        public string? ConsolidatedText { get; private set; }
        public string? EmbeddingId { get; private set; }
        public IReadOnlyList<string> Tags => _tags.AsReadOnly();
        private readonly List<string> _tags = new();

        // Uncommitted events
        private readonly List<MemoryEvent> _uncommittedEvents = new();
        public IReadOnlyList<MemoryEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();



        // Rehydrate
        public void LoadFromHistory(IEnumerable<MemoryEvent> history)
        {
            foreach (var evt in history.OrderBy(e => e.Version))
                Apply(evt, isNew: false);
        }

        // ─── Domain behaviours ───────────────────────────────────────────────────

        public void AddMemory(
            string rawText,
            MemorySource source,
            MemoryImportance importance = MemoryImportance.Medium,
            IEnumerable<string>? tags = null,
            string? userId = null,
            string? correlationId = null)
        {
            CoreGuard.NotNullOrWhiteSpace(rawText, nameof(rawText));
            if (source == MemorySource.Unknown) throw new DomainException("source must be a known value.");

            var newId = Id.Value == Guid.Empty ? MemoryId.New() : Id;
            var evt = new MemoryAddedEvent
            {
                AggregateId = newId,
                RawText = rawText,
                Source = source.ToString(),
                Importance = importance.ToString(),
                Tags = tags?.ToList() ?? new List<string>(),
                UserId = userId,
                CorrelationId = correlationId,
                EventType = nameof(MemoryAddedEvent)
            };

            Emit(evt);
        }

        public void UpdateRawText(string newText, string userId)
        {
            CoreGuard.NotNullOrWhiteSpace(newText, nameof(newText));
            if (Status == MemoryStatus.Deleted) throw new DomainException("Cannot update a deleted memory.");

            var evt = new MemoryUpdatedEvent
            {
                AggregateId = Id,
                MemoryId = Id,
                UpdatedFields = new Dictionary<string, string> { { nameof(RawText), newText } },
                UserId = userId,
                EventType = nameof(MemoryUpdatedEvent)
            };
            Emit(evt);
        }

        public void Compress(string compressedText, string compressionModel, int tokenCount, string userId)
        {
            CoreGuard.NotNullOrWhiteSpace(compressedText, nameof(compressedText));
            CoreGuard.NotNullOrWhiteSpace(compressionModel, nameof(compressionModel));
            if (tokenCount <= 0) throw new DomainException("tokenCount must be positive.");
            if (Status == MemoryStatus.Deleted) throw new DomainException("Cannot compress a deleted memory.");

            var evt = new MemoryCompressedEvent
            {
                AggregateId = Id,
                OriginalMemoryId = Id,
                CompressedText = compressedText,
                CompressionModel = compressionModel,
                TokenCount = tokenCount,
                UserId = userId,
                EventType = nameof(MemoryCompressedEvent)
            };
            Emit(evt);
        }

        /// <summary>
        /// Creates (or updates) a memory as the consolidated result of merging one or more source memories.
        /// When called on a fresh aggregate (no prior history), this establishes a brand-new stream.
        /// </summary>
        public void Consolidate(
            string consolidatedText,
            IEnumerable<Guid>? mergedMemoryIds,
            IEnumerable<string>? provenanceLinks,
            string userId)
        {
            CoreGuard.NotNullOrWhiteSpace(consolidatedText, nameof(consolidatedText));
            if (Status == MemoryStatus.Deleted) throw new DomainException("Cannot consolidate a deleted memory.");

            // For new streams the Id has not been set yet; use the one already given via constructor.
            var targetId = Id.Value == Guid.Empty ? MemoryId.New() : Id;
            var evt = new MemoryConsolidatedEvent
            {
                AggregateId = targetId,
                NewMemoryId = targetId,
                ConsolidatedText = consolidatedText,
                MergedMemoryIds = mergedMemoryIds?.ToList() ?? new List<Guid>(),
                ProvenanceLinks = provenanceLinks?.ToList() ?? new List<string>(),
                UserId = userId,
                EventType = nameof(MemoryConsolidatedEvent)
            };
            Emit(evt);
        }

        public void Delete(string reason, string userId)
        {
            CoreGuard.NotNullOrWhiteSpace(reason, nameof(reason));
            if (Status == MemoryStatus.Deleted) return;  // idempotent

            var evt = new MemoryDeletedEvent
            {
                AggregateId = Id,
                MemoryId = Id,
                Reason = reason,
                UserId = userId,
                EventType = nameof(MemoryDeletedEvent)
            };
            Emit(evt);
        }

        /// <summary>Records that this memory has been indexed in a vector store.</summary>
        public void MarkIndexed(string embeddingId, string vectorProvider, string userId)
        {
            CoreGuard.NotNullOrWhiteSpace(embeddingId, nameof(embeddingId));
            CoreGuard.NotNullOrWhiteSpace(vectorProvider, nameof(vectorProvider));
            if (Status == MemoryStatus.Deleted) throw new DomainException("Cannot index a deleted memory.");

            var evt = new MemoryIndexedEvent
            {
                AggregateId = Id,
                MemoryId = Id,
                EmbeddingId = embeddingId,
                VectorProvider = vectorProvider,
                UserId = userId,
                EventType = nameof(MemoryIndexedEvent)
            };
            Emit(evt);
        }

        /// <summary>Records that a snapshot of this aggregate's state has been persisted.</summary>
        public void CreateSnapshot(string snapshotPayload, int snapshotVersion, string userId)
        {
            CoreGuard.NotNullOrWhiteSpace(snapshotPayload, nameof(snapshotPayload));
            if (snapshotVersion <= 0) throw new DomainException("snapshotVersion must be positive.");

            var evt = new SnapshotCreatedEvent
            {
                AggregateId = Id,
                AggregateIdSnapshot = Id,
                SnapshotPayload = snapshotPayload,
                SnapshotVersion = snapshotVersion,
                UserId = userId,
                EventType = nameof(SnapshotCreatedEvent)
            };
            Emit(evt);
        }

        // ─── Emit + Apply ────────────────────────────────────────────────────────

        private void Emit(MemoryEvent evt)
        {
            evt.EventId = Guid.NewGuid();
            evt.Version = Version + 1;
            evt.Timestamp = DateTime.UtcNow;

            _uncommittedEvents.Add(evt);
            Apply(evt, isNew: true);
        }

        private void Apply(MemoryEvent @evt, bool isNew)
        {
            Version = @evt.Version;

            switch (@evt)
            {
                case MemoryAddedEvent added:
                    Id = new MemoryId(added.AggregateId);   // explicit cast now that operator is explicit
                    RawText = added.RawText;
                    Importance = Enum.TryParse<MemoryImportance>(added.Importance, ignoreCase: true, out var imp) ? imp : MemoryImportance.Medium;
                    _tags.Clear();
                    if (added.Tags != null) _tags.AddRange(added.Tags);
                    Status = MemoryStatus.Active;
                    break;

                case MemoryUpdatedEvent updated:
                    if (updated.UpdatedFields.TryGetValue(nameof(RawText), out var newText))
                        RawText = newText;
                    break;

                case MemoryCompressedEvent compressed:
                    CompressedText = compressed.CompressedText;
                    break;

                case MemoryConsolidatedEvent consolidated:
                    // For a new (genesis) consolidated stream, establish the Id.
                    if (Id.Value == Guid.Empty)
                        Id = new MemoryId(consolidated.AggregateId);
                    ConsolidatedText = consolidated.ConsolidatedText;
                    // The consolidated text becomes the primary readable content.
                    if (string.IsNullOrEmpty(RawText))
                        RawText = consolidated.ConsolidatedText;
                    Status = MemoryStatus.Active;
                    break;

                case MemoryIndexedEvent indexed:
                    EmbeddingId = indexed.EmbeddingId;
                    break;

                case MemoryDeletedEvent:
                    Status = MemoryStatus.Deleted;
                    break;

                case SnapshotCreatedEvent:
                    // Snapshot creation is recorded in the event stream for auditability;
                    // aggregate state itself does not change.
                    break;
            }

            // When replaying history, do not keep events as uncommitted.
            if (!isNew) return;
        }

        public void ClearUncommittedEvents() => _uncommittedEvents.Clear();


        // ─── Snapshot support ────────────────────────────────────────────────────

        public static MemoryAggregate FromSnapshot(MemorySnapshotDto dto)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));

            var agg = new MemoryAggregate(new MemoryId(dto.Id));

            agg.RawText = dto.RawText;
            agg.CompressedText = dto.CompressedText;
            agg.ConsolidatedText = dto.ConsolidatedText;
            agg.EmbeddingId = dto.EmbeddingId;
            agg.Importance = dto.Importance;

            agg._tags.Clear();
            if (dto.Tags != null) agg._tags.AddRange(dto.Tags);

            agg.Version = dto.Version;
            agg.Status = dto.Status;

            return agg;
        }
    }
}
