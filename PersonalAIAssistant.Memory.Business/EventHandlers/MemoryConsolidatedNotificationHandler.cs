using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Events;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Business.EventHandlers
{
    /// <summary>
    /// Event handler that reacts to MemoryConsolidatedEvent by sending outbound push notifications.
    /// Decoupled from the command handler to enforce the Single Responsibility Principle (SRP).
    /// </summary>
    public class MemoryConsolidatedNotificationHandler : IMemoryEventHandler<MemoryConsolidatedEvent>
    {
        private readonly INotificationSender _notifier;
        private readonly ILogger<MemoryConsolidatedNotificationHandler> _logger;

        public MemoryConsolidatedNotificationHandler(
            INotificationSender notifier,
            ILogger<MemoryConsolidatedNotificationHandler> logger)
        {
            _notifier = notifier;
            _logger = logger;
        }

        public async Task HandleAsync(MemoryConsolidatedEvent evt, CancellationToken ct)
        {
            if (evt == null) return;

            try
            {
                var mergedCount = evt.MergedMemoryIds?.Count ?? 0;
                await _notifier.SendAsync(
                    title: "Memory Consolidated",
                    body: $"**Merged {mergedCount} memories** for user `{evt.UserId}`.\n\n{evt.ConsolidatedText}",
                    ct: ct);

                _logger.LogInformation("Successfully dispatched notification for consolidated memory {MemoryId}", evt.AggregateId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Notification] Failed to send Teams notification for consolidated memory {MemoryId} — non-fatal.", evt.AggregateId);
            }
        }
    }
}
