using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PersonalAIAssistant.Memory.Api.Controllers;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Controllers;

public class AgentToolsControllerTests
{
    [Fact]
    public async Task GetFactHistory_UsesAuthenticatedUserAndReturnsFactHistory()
    {
        var expected = new List<TemporalFact>
        {
            new(Guid.NewGuid(), "Alice", "works_at", "Contoso", DateTime.UtcNow, Confidence: 0.8, Source: "Conversation")
        };
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(
                It.Is<GetTemporalFactHistoryQuery>(q =>
                    q.UserId == "user-123" && q.Subject == "Alice" && q.Predicate == "works_at"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = new AgentToolsController(Mock.Of<IAgentToolService>(), mediator.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Items["UserId"] = "user-123";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.GetFactHistory("Alice", "works_at", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(expected);
        mediator.Verify(m => m.Send(It.IsAny<GetTemporalFactHistoryQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", "works_at")]
    [InlineData("Alice", " ")]
    public async Task GetFactHistory_RejectsMissingSubjectOrPredicate(string subject, string predicate)
    {
        var mediator = new Mock<IMediator>();
        var controller = new AgentToolsController(Mock.Of<IAgentToolService>(), mediator.Object);

        var result = await controller.GetFactHistory(subject, predicate, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        mediator.Verify(m => m.Send(It.IsAny<GetTemporalFactHistoryQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
