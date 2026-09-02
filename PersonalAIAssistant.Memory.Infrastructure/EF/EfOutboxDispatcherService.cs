using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Infrastructure.EF
{
    public class EfOutboxDispatcherService : BackgroundService
    {
        private readonly EventStoreDbContext _db;
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly ILogger<EfOutboxDispatcherService> _logger;
        private readonly int _batchSize = 20;
        private readonly TimeSpan _delay = TimeSpan.FromSeconds(2);

        public EfOutboxDispatcherService(EventStoreDbContext db, IPublishEndpoint publishEndpoint, ILogger<EfOutboxDispatcherService> logger)
        {
            _db = db;
            _publishEndpoint = publishEndpoint;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var docs = await _db.OutboxMessages.Where(o => o.DispatchedAt == null).OrderBy(o => o.OccurredAt).Take(_batchSize).ToListAsync(stoppingToken);
                    if (docs.Count == 0)
                    {
                        await Task.Delay(_delay, stoppingToken);
                        continue;
                    }

                    foreach (var doc in docs)
                    {
                        try
                        {
                            var eventType = typeof(MemoryEvent).Assembly.GetTypes().FirstOrDefault(t => t.Name == doc.MessageType);
                            if (eventType == null)
                            {
                                _logger.LogWarning("Unknown outbox message type: {Type}", doc.MessageType);
                                doc.DispatchedAt = DateTime.UtcNow;
                                continue;
                            }

                            var evt = (MemoryEvent?)System.Text.Json.JsonSerializer.Deserialize(doc.Payload, eventType);
                            if (evt == null)
                            {
                                _logger.LogWarning("Failed to deserialize outbox payload for message {Id}", doc.MessageId);
                                doc.DispatchedAt = DateTime.UtcNow;
                                continue;
                            }

                            await _publishEndpoint.Publish((object)evt, stoppingToken);

                            doc.DispatchedAt = DateTime.UtcNow;
                            doc.Attempts += 1;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to publish EF outbox message {MessageId}", doc.MessageId);
                            doc.Attempts += 1;
                        }
                    }

                    await _db.SaveChangesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EF Outbox dispatcher encountered an error");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
    }
}
