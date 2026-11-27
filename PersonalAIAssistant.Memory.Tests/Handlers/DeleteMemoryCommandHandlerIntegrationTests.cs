using FluentAssertions;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Handlers;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Events;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Handlers;

public sealed class DeleteMemoryCommandHandlerIntegrationTests
{
    [Fact]
    public async Task Handle_DeletesMemoryInRealEventStore_AndRemainsIdempotent()
    {
        var eventStore = new InMemoryEventStore();
        var aggregate = new MemoryAggregate();
        aggregate.AddMemory("private memory content", MemorySource.User, userId: "delete-user");
        var memoryId = aggregate.Id.Value;
        var streamId = $"memory-{memoryId}";
        await eventStore.AppendEventsAsync(streamId, aggregate.UncommittedEvents.ToList(), 0, CancellationToken.None);
        var handler = new DeleteMemoryCommandHandler(eventStore);
        var command = new DeleteMemoryCommand(memoryId, "user requested deletion", "delete-user");

        await handler.Handle(command, CancellationToken.None);
        await handler.Handle(command, CancellationToken.None);

        var history = await eventStore.GetEventsAsync(streamId, CancellationToken.None);
        history.OfType<MemoryDeletedEvent>().Should().ContainSingle();
        history.Should().HaveCount(2);
        history.OfType<MemoryDeletedEvent>().Single().Reason.Should().Be("user requested deletion");
    }
}
