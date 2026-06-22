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
        // State
        public MemoryId Id { get; private set; }
        public int Version { get; private set; }
        public MemoryStatus Status { get; private set; } = MemoryStatus.Active;

        public string RawText { get; private set; } = string.Empty;
        public string? CompressedText { get; private set; }
        public string? ConsolidatedText { get; private set; }
        public string? EmbeddingId { get; private set; }
        public IReadOnlyList<string> Tags => _tags.AsReadOnly();
        private readonly List<string> _tags = new();

        // Uncommitted events
        private readonly List<MemoryEvent> _uncommittedEvents = new();
        public IReadOnlyList<MemoryEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

        // Constructors
        public MemoryAggregate(MemoryId id) => Id = id;
        public MemoryAggregate() { }

        // Rehydrate
        public void LoadFromHistory(IEnumerable<MemoryEvent> history)
        {
            foreach (var evt in history.OrderBy(e => e.Version))
                Apply(evt, isNew: false);
        }

        // Domain behaviors with invariants enforced
        public void AddMemory(string rawText, MemorySource source, IEnumerable<string>? tags = null, string? userId = null, string? correlationId = null)
        {
            CoreGuard.NotNullOrWhiteSpace(rawText, nameof(rawText));
            CoreGuard.NotDefault(source, nameof(source));

            var newId = Id.Equals(default(MemoryId)) ? MemoryId.New() : Id;
            var evt = new MemoryAddedEvent
            {
                AggregateId = newId,
                RawText = rawText,
                Source = source.ToString(),
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

        public void Delete(string reason, string userId)
        {
            CoreGuard.NotNullOrWhiteSpace(reason, nameof(reason));
            if (Status == MemoryStatus.Deleted) return;

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

        // Emit + Apply
        private void Emit(MemoryEvent evt)
        {
            // set metadata
            evt.EventId = Guid.NewGuid();
            evt.Version = Version + 1;
            evt.Timestamp = DateTime.UtcNow;

            _uncommittedEvents.Add(evt);
            Apply(evt, isNew: true);
        }

        private void Apply(MemoryEvent evt, bool isNew)
        {
            // apply state changes
            Version = evt.Version;

            switch (evt)
            {
                case MemoryAddedEvent added:
                    Id = added.AggregateId;
                    RawText = added.RawText;
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
                    ConsolidatedText = consolidated.ConsolidatedText;
                    break;

                case MemoryIndexedEvent indexed:
                    EmbeddingId = indexed.EmbeddingId;
                    break;

                case MemoryDeletedEvent _:
                    Status = MemoryStatus.Deleted;
                    break;

                case SnapshotCreatedEvent snapshot:
                    // snapshot handling is done by snapshot repository;
                    break;
            }

            // when replaying history, do not keep them as uncommitted
            if (!isNew) return;
        }

        public void ClearUncommittedEvents() => _uncommittedEvents.Clear();


        public static MemoryAggregate FromSnapshot(MemorySnapshotDto dto)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));

            // Create aggregate with the snapshot id
            var agg = new MemoryAggregate(new MemoryId(dto.Id));

            // Populate internal state (same class so private/protected members are accessible)
            agg.RawText = dto.RawText;
            agg.CompressedText = dto.CompressedText;
            agg.ConsolidatedText = dto.ConsolidatedText;
            agg.EmbeddingId = dto.EmbeddingId;

            agg._tags.Clear();
            if (dto.Tags != null) agg._tags.AddRange(dto.Tags);

            // restore version and status
            agg.Version = dto.Version;
            agg.Status = MemoryStatus.Active; // or restore if you store status in snapshot

            return agg;
        }
    }
}
