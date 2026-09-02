using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PersonalAIAssistant.Memory.Events;

namespace PersonalAIAssistant.Memory.Infrastructure.Messaging
{
    public class OutboxDispatcherService : BackgroundService
    {
        private readonly IMongoDatabase _database;
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly ILogger<OutboxDispatcherService> _logger;
        private readonly int _batchSize = 20;
        private readonly TimeSpan _delay = TimeSpan.FromSeconds(2);

        public OutboxDispatcherService(IMongoDatabase database, IPublishEndpoint publishEndpoint, ILogger<OutboxDispatcherService> logger)
        {
            _database = database;
            _publishEndpoint = publishEndpoint;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var collection = _database.GetCollection<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>("outbox");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var filter = Builders<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>.Filter.Eq(d => d.DispatchedAt, null as DateTime?);
                    var docs = await collection.Find(filter).SortBy(d => d.OccurredAt).Limit(_batchSize).ToListAsync(stoppingToken);

                    if (docs.Count == 0)
                    {
                        await Task.Delay(_delay, stoppingToken);
                        continue;
                    }

                    foreach (var doc in docs)
                    {
                        try
                        {
                            // Resolve event type by name from MemoryEvent assembly
                            var eventType = typeof(MemoryEvent).Assembly.GetTypes().FirstOrDefault(t => t.Name == doc.MessageType);
                            if (eventType == null)
                            {
                                _logger.LogWarning("Unknown outbox message type: {Type}", doc.MessageType);
                                // mark as dispatched to avoid retry loop
                                var update = Builders<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>.Update.Set(d => d.DispatchedAt, DateTime.UtcNow);
                                await collection.UpdateOneAsync(Builders<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>.Filter.Eq(d => d.Id, doc.Id), update, cancellationToken: stoppingToken);
                                continue;
                            }

                            var evt = (MemoryEvent?)System.Text.Json.JsonSerializer.Deserialize(doc.Payload, eventType);
                            if (evt == null)
                            {
                                _logger.LogWarning("Failed to deserialize outbox payload for message {Id}", doc.MessageId);
                                var update = Builders<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>.Update.Set(d => d.DispatchedAt, DateTime.UtcNow);
                                await collection.UpdateOneAsync(Builders<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>.Filter.Eq(d => d.Id, doc.Id), update, cancellationToken: stoppingToken);
                                continue;
                            }

                            await _publishEndpoint.Publish((object)evt, stoppingToken);

                            var dispatchedUpdate = Builders<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>.Update
                                .Set(d => d.DispatchedAt, DateTime.UtcNow)
                                .Inc(d => d.Attempts, 1);

                            await collection.UpdateOneAsync(Builders<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>.Filter.Eq(d => d.Id, doc.Id), dispatchedUpdate, cancellationToken: stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to publish outbox message {MessageId}", doc.MessageId);
                            var update = Builders<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>.Update.Inc(d => d.Attempts, 1);
                            await collection.UpdateOneAsync(Builders<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>.Filter.Eq(d => d.Id, doc.Id), update, cancellationToken: stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Outbox dispatcher encountered an error");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
    }
}
