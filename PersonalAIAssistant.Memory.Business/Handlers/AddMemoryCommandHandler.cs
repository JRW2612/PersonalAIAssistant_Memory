using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    public class AddMemoryCommandHandler : IRequestHandler<AddMemoryCommand, Guid>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;
        private readonly ITextChunker _chunker;
        private readonly Microsoft.Extensions.Options.IOptions<PersonalAIAssistant.Memory.Core.Models.AiProviderOptions> _options;

        public AddMemoryCommandHandler(
            IEventStore eventStore,
            IEventBus eventBus,
            ITextChunker chunker,
            Microsoft.Extensions.Options.IOptions<PersonalAIAssistant.Memory.Core.Models.AiProviderOptions> options)
        {
            _eventStore = eventStore;
            _eventBus = eventBus;
            _chunker = chunker;
            _options = options;
        }

        public async Task<Guid> Handle(AddMemoryCommand request, CancellationToken cancellationToken)
        {
            MemorySource source;
            string? customSourceTag = null;

            if (Enum.TryParse<MemorySource>(request.Source, ignoreCase: true, out var parsedSource)
                && Enum.IsDefined(parsedSource)
                && parsedSource != MemorySource.Unknown)
            {
                source = parsedSource;
            }
            else
            {
                source = MemorySource.System;
                if (!string.IsNullOrWhiteSpace(request.Source))
                {
                    customSourceTag = $"source:{request.Source.Trim()}";
                }
            }

            var opts = _options.Value.Chunking;
            var chunkOptions = new ChunkOptions(opts.MaxTokens, opts.OverlapTokens);

            var chunks = opts.Enabled
                ? _chunker.Chunk(request.RawText, chunkOptions)
                : new[] { new TextChunk(request.RawText, 0, request.RawText.Length) };

            var parentCorrelationId = request.CorrelationId ?? Guid.NewGuid().ToString();
            Guid firstAggregateId = Guid.Empty;

            var allEvents = new List<MemoryEvent>();

            foreach (var chunk in chunks)
            {
                var aggregate = new MemoryAggregate();

                var chunkTags = request.Tags?.ToList() ?? new List<string>();
                if (customSourceTag != null && !chunkTags.Contains(customSourceTag, StringComparer.OrdinalIgnoreCase))
                {
                    chunkTags.Add(customSourceTag);
                }
                if (chunks.Count > 1)
                {
                    chunkTags.Add($"chunk:{chunk.Index}");
                    chunkTags.Add($"parent:{parentCorrelationId}");
                }

                aggregate.AddMemory(
                    rawText: chunk.Text,
                    source: source,
                    importance: request.Importance,
                    tags: chunkTags,
                    userId: request.UserId,
                    correlationId: parentCorrelationId);

                var uncommittedEvents = aggregate.UncommittedEvents.ToList();
                if (!uncommittedEvents.Any()) continue;

                var streamId = $"memory-{aggregate.Id.Value}";

                // Create outbox messages for each event so they can be published reliably by the outbox dispatcher
                var outboxMessages = uncommittedEvents.Select(evt => new PersonalAIAssistant.Memory.Core.Messages.OutboxMessage
                {
                    MessageId = evt.EventId,
                    MessageType = evt.GetType().Name,
                    Payload = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType()),
                    OccurredAt = DateTime.UtcNow
                }).ToList();

                await _eventStore.AppendEventsWithOutboxAsync(streamId, uncommittedEvents, 0, outboxMessages, cancellationToken);
                allEvents.AddRange(uncommittedEvents);

                aggregate.ClearUncommittedEvents();

                if (firstAggregateId == Guid.Empty)
                {
                    firstAggregateId = aggregate.Id.Value;
                }
            }

            // Events are persisted and outbox entries created. A background outbox dispatcher will publish them to RabbitMQ.

            return firstAggregateId == Guid.Empty ? Guid.NewGuid() : firstAggregateId;
        }
    }
}
