using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Handlers;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Handlers;

public class AppendConversationTurnCommandHandlerTests
{
    [Fact]
    public async Task Handle_UserTurn_StoresTurnAndFormsMemories()
    {
        var buffer = new Mock<IConversationBuffer>();
        var formation = new Mock<IAutomaticMemoryFormationService>();
        var handler = new AppendConversationTurnCommandHandler(buffer.Object, formation.Object, NullLogger<AppendConversationTurnCommandHandler>.Instance);

        var result = await handler.Handle(
            new AppendConversationTurnCommand("user-123", "session-1", "user", "I prefer green tea."),
            CancellationToken.None);

        result.Should().BeTrue();
        buffer.Verify(b => b.AppendTurnAsync("user-123", "session-1", "user", "I prefer green tea.", It.IsAny<CancellationToken>()), Times.Once);
        formation.Verify(s => s.FormMemoriesFromUserTurnAsync("user-123", "I prefer green tea.", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("assistant")]
    [InlineData("system")]
    public async Task Handle_NonUserTurn_DoesNotFormMemories(string role)
    {
        var buffer = new Mock<IConversationBuffer>();
        var formation = new Mock<IAutomaticMemoryFormationService>();
        var handler = new AppendConversationTurnCommandHandler(buffer.Object, formation.Object, NullLogger<AppendConversationTurnCommandHandler>.Instance);

        await handler.Handle(new AppendConversationTurnCommand("user-123", "session-1", role, "I prefer green tea."), CancellationToken.None);

        formation.Verify(s => s.FormMemoriesFromUserTurnAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
