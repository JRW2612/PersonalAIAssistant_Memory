using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Queries;
using PersonalAIAssistant.Memory.Business.Services;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Handlers;

public class AgentToolServiceTests
{
    private readonly Mock<IMediator> _mockMediator;
    private readonly Mock<IToolSchemaValidator> _mockValidator;
    private readonly AgentToolService _service;

    public AgentToolServiceTests()
    {
        _mockMediator = new Mock<IMediator>();
        _mockValidator = new Mock<IToolSchemaValidator>();
        _service = new AgentToolService(
            _mockMediator.Object,
            _mockValidator.Object,
            NullLogger<AgentToolService>.Instance);
    }

    [Fact]
    public void GetToolDefinitions_ReturnsAllStandardTools()
    {
        var tools = _service.GetToolDefinitions();

        tools.Should().HaveCount(5);
        tools.Select(t => t.Name).Should().Contain(new[]
        {
            "temporal_fact_upsert",
            "core_memory_append",
            "core_memory_replace",
            "archival_memory_insert",
            "archival_memory_search"
        });
    }

    [Fact]
    public async Task ExecuteToolAsync_TemporalFactUpsert_DispatchesAssertTemporalFactCommand()
    {
        var expectedFactId = Guid.NewGuid();
        _mockMediator
            .Setup(m => m.Send(It.IsAny<AssertTemporalFactCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedFactId);

        var args = JsonDocument.Parse("""
        {
            "subject": "Bob",
            "predicate": "job_title",
            "object": "Principal Engineer",
            "confidence": 0.98
        }
        """).RootElement;

        var request = new AgentToolExecutionRequest("temporal_fact_upsert", args);
        var response = await _service.ExecuteToolAsync("user-456", request);

        response.Success.Should().BeTrue();
        response.ToolName.Should().Be("temporal_fact_upsert");

        _mockMediator.Verify(m => m.Send(It.Is<AssertTemporalFactCommand>(cmd =>
            cmd.Subject == "Bob" &&
            cmd.Predicate == "job_title" &&
            cmd.Object == "Principal Engineer" &&
            cmd.UserId == "user-456"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteToolAsync_CoreMemoryAppend_DispatchesAppendConversationTurnCommand()
    {
        _mockMediator
            .Setup(m => m.Send(It.IsAny<AppendConversationTurnCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var args = JsonDocument.Parse("""
        {
            "role": "assistant",
            "content": "Noted your preference for Ethiopian Yirgacheffe.",
            "sessionId": "morning-session"
        }
        """).RootElement;

        var request = new AgentToolExecutionRequest("core_memory_append", args);
        var response = await _service.ExecuteToolAsync("user-456", request);

        response.Success.Should().BeTrue();
        _mockMediator.Verify(m => m.Send(It.Is<AppendConversationTurnCommand>(cmd =>
            cmd.Role == "assistant" &&
            cmd.Content.Contains("Yirgacheffe") &&
            cmd.SessionId == "morning-session" &&
            cmd.UserId == "user-456"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteToolAsync_ArchivalMemorySearch_DispatchesRetrieveMemoriesQuery()
    {
        var expectedResult = new FusedMemoryPrompt(
            SystemContext: "context",
            Sources: new List<RetrievedMemory>());

        _mockMediator
            .Setup(m => m.Send(It.IsAny<RetrieveMemoriesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var args = JsonDocument.Parse("""
        {
            "query": "coffee preferences",
            "topK": 4
        }
        """).RootElement;

        var request = new AgentToolExecutionRequest("archival_memory_search", args);
        var response = await _service.ExecuteToolAsync("user-456", request);

        response.Success.Should().BeTrue();
        _mockMediator.Verify(m => m.Send(It.Is<RetrieveMemoriesQuery>(q =>
            q.QueryText == "coffee preferences" &&
            q.TopK == 4 &&
            q.UserId == "user-456"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteToolAsync_UnknownTool_ReturnsFailure()
    {
        var args = JsonDocument.Parse("{}").RootElement;
        var request = new AgentToolExecutionRequest("unknown_tool_call", args);

        var response = await _service.ExecuteToolAsync("user-456", request);

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("Unknown tool");
    }
}
