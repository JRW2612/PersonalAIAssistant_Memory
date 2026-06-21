using PersonalAIAssistant.Memory.Events;
namespace PersonalAIAssistant.Memory.Core.Domains
{
    public class MemoryAggregate
    {
        public Guid Id { get; protected set; }
        public int Version { get; protected set; }
        public bool IsDeleted { get; protected set; }

        public string RawText { get; protected set; }
        public string CompressedText { get; protected set; }
        public string ConsolidatedText { get; protected set; }
        public string EmbeddingId { get; protected set; }
        public List<string> Tags { get; protected set; } = new();
        private readonly List<MemoryEvent> _uncommittedEvents = new();
        public IReadOnlyList<MemoryEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

        public MemoryAggregate(Guid id)
        {
            Id = id;
        }

        public MemoryAggregate() { }

        public void LoadFromHistory(IEnumerable<MemoryEvent> history)
        {
            foreach (var evt in history)
            {
                Apply(evt);
            }
        }

        public void AddMemory(string rawText, string source, List<string> tags)
        {
            var evt = new MemoryAddedEvent
            {
                AggregateId = Id,
                RawText = rawText,
                Source = source,
                Tags = tags ?? new List<string>(),
                EventType = nameof(MemoryAddedEvent)
            };
            Emit(evt);
        }

        public void UpdateMemory(Dictionary<string, string> updatedFields)
        {
            if (IsDeleted) throw new InvalidOperationException("Cannot update a deleted memory.");

            var evt = new MemoryUpdatedEvent
            {
                AggregateId = Id,
                MemoryId = Id,
                UpdatedFields = updatedFields,
                EventType = nameof(MemoryUpdatedEvent)
            };
            Emit(evt);
        }

        public void CompressMemory(string compressedText, string compressionModel, int tokenCount)
        {
            if (IsDeleted) throw new InvalidOperationException("Cannot compress a deleted memory.");

            var evt = new MemoryCompressedEvent
            {
                AggregateId = Id,
                OriginalMemoryId = Id,
                CompressedText = compressedText,
                CompressionModel = compressionModel,
                TokenCount = tokenCount,
                EventType = nameof(MemoryCompressedEvent)
            };
            Emit(evt);
        }

        public void Consolidate(List<Guid> mergedMemoryIds, string consolidatedText, List<string> provenanceLinks)
        {
            var evt = new MemoryConsolidatedEvent
            {
                AggregateId = Id,
                NewMemoryId = Id,
                MergedMemoryIds = mergedMemoryIds,
                ConsolidatedText = consolidatedText,
                ProvenanceLinks = provenanceLinks,
                EventType = nameof(MemoryConsolidatedEvent)
            };
            Emit(evt);
        }

        public void MarkAsIndexed(string embeddingId, string vectorProvider)
        {
            if (IsDeleted) throw new InvalidOperationException("Cannot index a deleted memory.");

            var evt = new MemoryIndexedEvent
            {
                AggregateId = Id,
                MemoryId = Id,
                EmbeddingId = embeddingId,
                VectorProvider = vectorProvider,
                EventType = nameof(MemoryIndexedEvent)
            };
            Emit(evt);
        }

        public void CreateSnapshot(string snapshotPayload)
        {
            var evt = new SnapshotCreatedEvent
            {
                AggregateId = Id,
                AggregateIdSnapshot = Id,
                SnapshotPayload = snapshotPayload,
                SnapshotVersion = Version,
                EventType = nameof(SnapshotCreatedEvent)
            };
            Emit(evt);
        }

        public void DeleteMemory(string reason)
        {
            if (IsDeleted) return;

            var evt = new MemoryDeletedEvent
            {
                AggregateId = Id,
                MemoryId = Id,
                Reason = reason,
                EventType = nameof(MemoryDeletedEvent)
            };
            Emit(evt);
        }

        public void ClearUncommittedEvents()
        {
            _uncommittedEvents.Clear();
        }

        private void Emit(MemoryEvent evt)
        {
            evt.EventId = Guid.NewGuid();
            evt.Version = Version + 1;
            evt.Timestamp = DateTime.UtcNow;

            _uncommittedEvents.Add(evt);
            Apply(evt);
        }

        private void Apply(MemoryEvent evt)
        {
            Version = evt.Version;

            switch (evt)
            {
                case MemoryAddedEvent added:
                    Id = added.AggregateId;
                    RawText = added.RawText;
                    Tags = added.Tags ?? new List<string>();
                    break;

                case MemoryUpdatedEvent updated:
                    if (updated.UpdatedFields.TryGetValue("RawText", out var newText))
                    {
                        RawText = newText;
                    }
                    break;

                case MemoryCompressedEvent compressed:
                    CompressedText = compressed.CompressedText;
                    break;

                case MemoryConsolidatedEvent consolidated:
                    Id = consolidated.AggregateId;
                    ConsolidatedText = consolidated.ConsolidatedText;
                    break;

                case MemoryIndexedEvent indexed:
                    EmbeddingId = indexed.EmbeddingId;
                    break;

                case MemoryDeletedEvent deleted:
                    IsDeleted = true;
                    break;

                case SnapshotCreatedEvent _:
                    // Snapshots typically don't mutate state themselves in the Apply method
                    break;
            }
        }
    }
}