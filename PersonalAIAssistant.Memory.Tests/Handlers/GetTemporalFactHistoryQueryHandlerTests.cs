using FluentAssertions;
using Moq;
using PersonalAIAssistant.Memory.Business.Handlers;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Models;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Handlers;

public class GetTemporalFactHistoryQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsHistoryScopedToRequestedUserAndFact()
    {
        var expected = new List<TemporalFact>
        {
            new(Guid.NewGuid(), "Alice", "works_at", "Contoso", DateTime.UtcNow, Confidence: 0.8, Source: "Conversation"),
            new(Guid.NewGuid(), "Alice", "works_at", "Fabrikam", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(-1), IsCurrent: false, Confidence: 0.95, Source: "User")
        };
        var repository = new Mock<ITemporalFactRepository>();
        repository
            .Setup(r => r.GetFactHistoryAsync("user-123", "Alice", "works_at", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var handler = new GetTemporalFactHistoryQueryHandler(repository.Object);

        var result = await handler.Handle(
            new GetTemporalFactHistoryQuery("user-123", "Alice", "works_at"),
            CancellationToken.None);

        result.Should().Equal(expected);
        repository.Verify(r => r.GetFactHistoryAsync("user-123", "Alice", "works_at", It.IsAny<CancellationToken>()), Times.Once);
    }
}
