using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Business.EventHandlers
{
    /// <summary>
    /// Reacts specifically to newly-added memories by generating and storing a vector embedding,
    /// then records the outcome as a MemoryIndexedEvent.
    /// Implements typed IMemoryEventHandler<MemoryAddedEvent> following ISP.
    /// </summary>
    public class EmbeddingIndexingEventHandler : IMemoryEventHandler<MemoryAddedEvent>
    {
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorMemoryRepository _vectorRepo;
        private readonly IMediator _mediator;
        private readonly ILogger<EmbeddingIndexingEventHandler> _logger;

        public EmbeddingIndexingEventHandler(
            IEmbeddingService embeddingService,
            IVectorMemoryRepository vectorRepo,
            IMediator mediator,
            ILogger<EmbeddingIndexingEventHandler> logger)
        {
            _embeddingService = embeddingService;
            _vectorRepo = vectorRepo;
            _mediator = mediator;
            _logger = logger;
        }

        public async Task HandleAsync(MemoryAddedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;

            try
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(evt.RawText, ct);
                await _vectorRepo.UpsertAsync(evt.AggregateId, embedding.EmbeddingId, embedding.Vector, evt.UserId, ct);
                await _mediator.Send(new MemoryIndexedCommand(evt.AggregateId, embedding.EmbeddingId, embedding.Provider), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while handling MemoryAddedEvent for AggregateId {AggregateId}", evt.AggregateId);
                throw;
            }
        }
    }
}
