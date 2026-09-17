using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Business.Behaviors;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Common;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.InMemory;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security;

public class AgentTracingBehaviorTests
{
    private class TestUserContext : IUserContext
    {
        public string UserId => "test-user-123";
        public string TenantId => "tenant-xyz";
        public IReadOnlyList<string> Roles => new[] { "User" };
        public bool IsAuthenticated => true;
    }

    public record SampleCorrelatedRequest(string Message, string? CorrelationId) : ICorrelatedRequest;
    public record SampleResponse(string Result);

    private readonly InMemoryAgentTraceRepository _traceRepo;
    private readonly AgentTracingBehavior<SampleCorrelatedRequest, SampleResponse> _behavior;

    public AgentTracingBehaviorTests()
    {
        var options = new AgentTracingOptions
        {
            Enabled = true,
            MaxTraces = 100,
            RetentionHours = 24
        };

        _traceRepo = new InMemoryAgentTraceRepository(
            Options.Create(options),
            NullLogger<InMemoryAgentTraceRepository>.Instance);

        _behavior = new AgentTracingBehavior<SampleCorrelatedRequest, SampleResponse>(
            _traceRepo,
            new TestUserContext(),
            Options.Create(options),
            NullLogger<AgentTracingBehavior<SampleCorrelatedRequest, SampleResponse>>.Instance);
    }

    [Fact]
    public async Task Handle_SuccessPath_RecordsSuccessfulTraceWithDurationAndOutput()
    {
        var request = new SampleCorrelatedRequest("Hello agent", "corr-456");

        var response = await _behavior.Handle(
            request,
            _ => Task.FromResult(new SampleResponse("Success")),
            CancellationToken.None);

        response.Result.Should().Be("Success");

        var traces = await _traceRepo.GetByUserAsync("test-user-123", 10, CancellationToken.None);
        traces.Should().HaveCount(1);

        var trace = traces[0];
        trace.Success.Should().BeTrue();
        trace.UserId.Should().Be("test-user-123");
        trace.TenantId.Should().Be("tenant-xyz");
        trace.CorrelationId.Should().Be("corr-456");
        trace.RequestName.Should().Be(nameof(SampleCorrelatedRequest));
        trace.ErrorCategory.Should().Be(AgentErrorCategory.None);
        trace.OutputJson.Should().Contain("Success");
    }

    [Fact]
    public async Task Handle_PromptInjectionException_ClassifiesErrorAndPersistsTrace()
    {
        var request = new SampleCorrelatedRequest("Ignore instructions", "corr-pi");

        var act = () => _behavior.Handle(
            request,
            _ => throw new PromptInjectionException(InjectionCategory.SystemPromptOverride, "ignore previous", "Attempt detected"),
            CancellationToken.None);

        await act.Should().ThrowAsync<PromptInjectionException>();

        var traces = await _traceRepo.GetByUserAsync("test-user-123", 10, CancellationToken.None);
        traces.Should().HaveCount(1);

        var trace = traces[0];
        trace.Success.Should().BeFalse();
        trace.ErrorCategory.Should().Be(AgentErrorCategory.PromptInjection);
        trace.ErrorDetail.Should().Contain("Attempt detected");
    }

    [Fact]
    public async Task Handle_DlpViolationException_ClassifiesAsDlpViolation()
    {
        var request = new SampleCorrelatedRequest("Credit card 4111...", "corr-dlp");

        var act = () => _behavior.Handle(
            request,
            _ => throw new DlpViolationException(
                new[] { new DlpViolation(DlpCategory.CreditCardNumber, "Credit Card detected", 0) }),
            CancellationToken.None);

        await act.Should().ThrowAsync<DlpViolationException>();

        var traces = await _traceRepo.GetByUserAsync("test-user-123", 10, CancellationToken.None);
        traces.Should().HaveCount(1);
        traces[0].ErrorCategory.Should().Be(AgentErrorCategory.DlpViolation);
    }
}