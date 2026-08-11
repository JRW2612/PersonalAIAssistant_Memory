using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Interfaces.Mongo;

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
            if (!Enum.TryParse<MemorySource>(request.Source, ignoreCase: true, out var source) || !Enum.IsDefined(source))
                throw new DomainException($"Unsupported memory source: '{request.Source}'.");

            var opts = _options.Value.Chunking;
            var chunkOptions = new ChunkOptions(opts.MaxTokens, opts.OverlapTokens);
            
            var chunks = opts.Enabled 
                ? _chunker.Chunk(request.RawText, chunkOptions) 
                : new[] { new TextChunk(request.RawText, 0, request.RawText.Length) };

            var parentCorrelationId = request.CorrelationId ?? Guid.NewGuid().ToString();
            Guid firstAggregateId = Guid.Empty;

            var allEvents = new List<PersonalAIAssistant.Memory.Events.MemoryEvent>();

            foreach (var chunk in chunks)
            {
                var aggregate = new MemoryAggregate();
                
                var chunkTags = request.Tags?.ToList() ?? new List<string>();
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
                
                await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, 0, cancellationToken);
                allEvents.AddRange(uncommittedEvents);
                
                aggregate.ClearUncommittedEvents();
                
                if (firstAggregateId == Guid.Empty)
                {
                    firstAggregateId = aggregate.Id.Value;
                }
            }

            if (allEvents.Any())
            {
                await _eventBus.PublishAsync(allEvents, cancellationToken);
            }

            return firstAggregateId == Guid.Empty ? Guid.NewGuid() : firstAggregateId;
        }
    }
}
