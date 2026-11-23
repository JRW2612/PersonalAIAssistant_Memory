using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Events;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;

namespace PersonalAIAssistant.Memory.Infrastructure.Messaging
{
    public sealed class InMemoryOutboxDispatcherService : BackgroundService
    {
        private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

        private readonly InMemoryEventStore _eventStore;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<InMemoryOutboxDispatcherService> _logger;

        public InMemoryOutboxDispatcherService(
            IEventStore eventStore,
            IServiceScopeFactory scopeFactory,
            ILogger<InMemoryOutboxDispatcherService> logger)
        {
            _eventStore = eventStore as InMemoryEventStore
                ?? throw new InvalidOperationException("The in-memory outbox dispatcher requires InMemoryEventStore.");
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_eventStore.TryPeekOutboxMessage(out var message))
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                try
                {
                    var eventType = typeof(MemoryEvent).Assembly.GetTypes().FirstOrDefault(type =>
                        type.Name == message!.MessageType &&
                        !type.IsAbstract &&
                        typeof(MemoryEvent).IsAssignableFrom(type));
                    if (eventType == null)
                    {
                        throw new InvalidOperationException(
                            $"Unknown in-memory outbox message type '{message!.MessageType}' for message {message.MessageId}.");
                    }

                    var memoryEvent = (MemoryEvent?)System.Text.Json.JsonSerializer.Deserialize(message!.Payload, eventType);
                    if (memoryEvent == null)
                    {
                        throw new InvalidOperationException(
                            $"Failed to deserialize in-memory outbox payload for message {message.MessageId}.");
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
                    await eventBus.PublishAsync(memoryEvent, stoppingToken);

                    if (!_eventStore.TryDequeueOutboxMessage(out _))
                    {
                        throw new InvalidOperationException("The dispatched in-memory outbox message could not be acknowledged.");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to dispatch in-memory outbox message {MessageId}", message!.MessageId);
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }
        }
    }
}
