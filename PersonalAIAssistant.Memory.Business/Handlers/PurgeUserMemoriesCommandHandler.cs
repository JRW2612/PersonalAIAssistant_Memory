using MediatR;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    /// <summary>
    /// Handles GDPR Right to Erasure and employee offboarding.
    /// SRP: orchestrates user memory purge across all 3 storage tiers.
    /// </summary>
    public sealed class PurgeUserMemoriesCommandHandler
        : IRequestHandler<PurgeUserMemoriesCommand, int>
    {
        private readonly IReadModelRepository _readRepo;
        private readonly IRetentionQueryStore _retentionStore;
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;
        private readonly IVectorMemoryRepository _vectorRepo;
        private readonly IUserContext _userContext;
        private readonly ILogger<PurgeUserMemoriesCommandHandler> _logger;

        public PurgeUserMemoriesCommandHandler(
            IReadModelRepository readRepo,
            IRetentionQueryStore retentionStore,
            IEventStore eventStore,
            IEventBus eventBus,
            IVectorMemoryRepository vectorRepo,
            IUserContext userContext,
            ILogger<PurgeUserMemoriesCommandHandler> logger)
        {
            _readRepo = readRepo;
            _retentionStore = retentionStore;
            _eventStore = eventStore;
            _eventBus = eventBus;
            _vectorRepo = vectorRepo;
            _userContext = userContext;
            _logger = logger;
        }

        public async Task<int> Handle(PurgeUserMemoriesCommand request, CancellationToken cancellationToken)
        {
            // RBAC: Only ComplianceAuditor or Admin may purge
            if (!_userContext.IsAdmin && !_userContext.IsAuditor)
                throw new UnauthorizedAccessException(
                    "Purging user memories requires ComplianceAuditor or Admin role.");

            var memories = await _readRepo.GetMemoriesByUserAsync(request.TargetUserId, cancellationToken);
            if (!memories.Any())
            {
                _logger.LogInformation("[GDPR] No memories found for user {UserId}", request.TargetUserId);
                return 0;
            }

            var purgedCount = 0;
            foreach (var memory in memories)
            {
                try
                {
                    var streamId = $"memory-{memory.MemoryId}";
                    var history = await _eventStore.GetEventsAsync(streamId, cancellationToken);
                    var aggregate = new MemoryAggregate();
                    aggregate.LoadFromHistory(history);

                    if (aggregate.IsLegalHold)
                    {
                        _logger.LogWarning(
                            "[GDPR] Skipping memory {MemoryId} — currently under legal hold: {Reason}",
                            memory.MemoryId, aggregate.LegalHoldReason);
                        continue;
                    }

                    aggregate.Delete(request.PurgeReason, request.RequestedByUserId);
                    var newEvents = aggregate.UncommittedEvents.ToList();
                    if (newEvents.Any())
                    {
                        var expectedVersion = aggregate.Version - newEvents.Count;
                        await _eventStore.AppendEventsAsync(streamId, newEvents, expectedVersion, cancellationToken);
                        await _eventBus.PublishAsync(newEvents, cancellationToken);
                    }

                    await _vectorRepo.DeleteAsync(memory.MemoryId, cancellationToken);
                    purgedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GDPR] Failed to purge memory {MemoryId} for user {UserId}",
                        memory.MemoryId, request.TargetUserId);
                }
            }

            // Emit audit event
            var purgeAuditEvent = new UserMemoriesPurgedEvent
            {
                AggregateId = Guid.NewGuid(),
                PurgedByUserId = request.RequestedByUserId,
                PurgeReason = request.PurgeReason,
                MemoryCount = purgedCount,
                UserId = request.RequestedByUserId,
                EventType = nameof(UserMemoriesPurgedEvent)
            };
            await _eventBus.PublishAsync(new[] { (MemoryEvent)purgeAuditEvent }, cancellationToken);

            _logger.LogInformation(
                "[GDPR] Purged {Count} memories for user {TargetUserId}, requested by {RequestedBy}, reason: {Reason}",
                purgedCount, request.TargetUserId, request.RequestedByUserId, request.PurgeReason);

            return purgedCount;
        }
    }
}
