using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using PersonalAIAssistant.Memory.Infrastructure.EF;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;

namespace PersonalAIAssistant.Memory.Infrastructure.Messaging
{
    public class OutboxCleanupService : BackgroundService
    {
        private readonly ILogger<OutboxCleanupService> _logger;
        private readonly IServiceProvider _services;
        private readonly OutboxOptions _options;

        public OutboxCleanupService(IServiceProvider services, IOptions<OutboxOptions> options, ILogger<OutboxCleanupService> logger)
        {
            _services = services;
            _logger = logger;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, _options.CleanupIntervalSeconds));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cutoff = DateTime.UtcNow.AddDays(-Math.Max(0, _options.RetentionDays));

                    using (var scope = _services.CreateScope())
                    {
                        // EF outbox cleanup (if EventStoreDbContext registered)
                        try
                        {
                            var efDb = scope.ServiceProvider.GetService(typeof(EventStoreDbContext)) as EventStoreDbContext;
                            if (efDb != null)
                            {
                                var old = await efDb.OutboxMessages.Where(o => o.DispatchedAt != null && o.DispatchedAt < cutoff).ToListAsync(stoppingToken);
                                if (old.Count > 0)
                                {
                                    efDb.OutboxMessages.RemoveRange(old);
                                    await efDb.SaveChangesAsync(stoppingToken);
                                    _logger.LogInformation("OutboxCleanup: removed {Count} dispatched EF outbox messages older than {Cutoff}", old.Count, cutoff);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "EF outbox cleanup failed");
                        }

                        // Mongo outbox cleanup (if IMongoDatabase registered)
                        try
                        {
                            var mongoDb = scope.ServiceProvider.GetService(typeof(IMongoDatabase)) as IMongoDatabase;
                            if (mongoDb != null)
                            {
                                var collection = mongoDb.GetCollection<OutboxDocument>("outbox");
                                var filter = Builders<OutboxDocument>.Filter.And(
                                    Builders<OutboxDocument>.Filter.Ne(d => d.DispatchedAt, null as DateTime?),
                                    Builders<OutboxDocument>.Filter.Lt(d => d.DispatchedAt, cutoff)
                                );
                                var result = await collection.DeleteManyAsync(filter, cancellationToken: stoppingToken);
                                if (result.DeletedCount > 0)
                                {
                                    _logger.LogInformation("OutboxCleanup: removed {Count} dispatched Mongo outbox messages older than {Cutoff}", result.DeletedCount, cutoff);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Mongo outbox cleanup failed");
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Outbox cleanup encountered an error");
                }

                await Task.Delay(interval, stoppingToken);
            }
        }
    }
}
