using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Core.Messages;
using PersonalAIAssistant.Memory.Events;
using PersonalAIAssistant.Memory.Infrastructure.Messaging;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure;

public class InMemoryOutboxDispatcherTests
{
    [Fact]
    public async Task DispatchesAndAcknowledgesOutboxEventThroughEventBus()
    {
        var eventStore = new InMemoryEventStore();
        var expectedEvent = new MemoryAddedEvent
        {
            AggregateId = Guid.NewGuid(),
            RawText = "test memory"
        };
        await eventStore.AppendEventsWithOutboxAsync(
            $"memory-{expectedEvent.AggregateId}",
            new MemoryEvent[] { expectedEvent },
            0,
            new[]
            {
                new OutboxMessage
                {
                    MessageId = expectedEvent.EventId,
                    MessageType = nameof(MemoryAddedEvent)
                }
            },
            CancellationToken.None);

        var publishedEvent = new TaskCompletionSource<MemoryEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MemoryEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MemoryEvent, CancellationToken>((memoryEvent, _) => publishedEvent.TrySetResult(memoryEvent))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventStore>(eventStore);
        services.AddScoped<IEventBus>(_ => eventBus.Object);
        using var provider = services.BuildServiceProvider();
        var dispatcher = new InMemoryOutboxDispatcherService(
            provider.GetRequiredService<IEventStore>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<InMemoryOutboxDispatcherService>>());

        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            var actualEvent = await publishedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsType<MemoryAddedEvent>(actualEvent);
            Assert.Equal(expectedEvent.EventId, actualEvent.EventId);
            Assert.Equal(expectedEvent.AggregateId, actualEvent.AggregateId);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (eventStore.TryPeekOutboxMessage(out _) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.False(eventStore.TryPeekOutboxMessage(out _));
            eventBus.Verify(bus => bus.PublishAsync(It.IsAny<MemoryEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }
}
