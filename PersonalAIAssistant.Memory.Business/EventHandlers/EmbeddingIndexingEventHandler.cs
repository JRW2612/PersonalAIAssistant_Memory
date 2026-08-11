using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Business.EventHandlers
{  /// <summary>
   /// Reacts to newly-added memories by generating and storing a vector embedding, then records
   /// the outcome as a MemoryIndexedEvent. This is what actually makes IEmbeddingService and
   /// IVectorMemoryRepository do something end-to-end instead of being unused stubs.
   /// </summary>
    public class EmbeddingIndexingEventHandler : IMemoryEventHandler
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

        public async Task HandleAsync(MemoryEvent evt, CancellationToken ct)
        {
            if (evt is not MemoryAddedEvent memoryAddedEvent)
            {
                _logger.LogWarning("Received event of type {EventType}, but only MemoryAddedEvent is handled. Ignoring.", evt.EventType);
                return;
            }

            // Continue with embedding generation and storage logic
            try
            {
                // Generate embedding
                var embedding = await _embeddingService.GenerateEmbeddingAsync(memoryAddedEvent.RawText, ct);
                // Store embedding in vector repository with tenant separation
                await _vectorRepo.UpsertAsync(memoryAddedEvent.AggregateId, embedding.EmbeddingId, embedding.Vector, memoryAddedEvent.UserId, ct);
                await _mediator.Send(new MemoryIndexedCommand(memoryAddedEvent.AggregateId, embedding.EmbeddingId, embedding.Provider), ct);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while handling MemoryAddedEvent for AggregateId {AggregateId}", memoryAddedEvent.AggregateId);
                throw; // Optionally rethrow or handle the exception as needed
            }
        }
    }
}
