using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.InMemory;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure;

public class InMemoryAgentTraceRepositoryTests
{
    private readonly InMemoryAgentTraceRepository _repo;
    private readonly AgentTracingOptions _options;

    public InMemoryAgentTraceRepositoryTests()
    {
        _options = new AgentTracingOptions
        {
            Enabled = true,
            MaxTraces = 3,
            RetentionHours = 24
        };

        _repo = new InMemoryAgentTraceRepository(
            Options.Create(_options),
            NullLogger<InMemoryAgentTraceRepository>.Instance);
    }

    [Fact]
    public async Task AddAsync_And_GetByIdAsync_RetrievesStoredTrace()
    {
        var traceId = Guid.NewGuid();
        var trace = new AgentTrace
        {
            TraceId = traceId,
            UserId = "user-alice",
            RequestName = "AddMemoryCommand",
            ToolName = "archival_memory_insert",
            StartedAt = DateTimeOffset.UtcNow,
            Success = true
        };

        await _repo.AddAsync(trace, CancellationToken.None);

        var retrieved = await _repo.GetByIdAsync(traceId, CancellationToken.None);
        retrieved.Should().NotBeNull();
        retrieved!.UserId.Should().Be("user-alice");
        retrieved.RequestName.Should().Be("AddMemoryCommand");
        retrieved.Success.Should().BeTrue();
    }

    [Fact]
    public async Task AddAsync_ExceedingCapacity_EvictsOldestFifo()
    {
        var trace1 = new AgentTrace { TraceId = Guid.NewGuid(), UserId = "u1", StartedAt = DateTimeOffset.UtcNow.AddMinutes(-3), Success = true };
        var trace2 = new AgentTrace { TraceId = Guid.NewGuid(), UserId = "u1", StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2), Success = true };
        var trace3 = new AgentTrace { TraceId = Guid.NewGuid(), UserId = "u1", StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1), Success = true };
        var trace4 = new AgentTrace { TraceId = Guid.NewGuid(), UserId = "u1", StartedAt = DateTimeOffset.UtcNow, Success = true };

        await _repo.AddAsync(trace1, CancellationToken.None);
        await _repo.AddAsync(trace2, CancellationToken.None);
        await _repo.AddAsync(trace3, CancellationToken.None);
        // Max capacity is 3. Adding trace4 should evict trace1.
        await _repo.AddAsync(trace4, CancellationToken.None);

        var retrieved1 = await _repo.GetByIdAsync(trace1.TraceId, CancellationToken.None);
        var retrieved2 = await _repo.GetByIdAsync(trace2.TraceId, CancellationToken.None);
        var retrieved4 = await _repo.GetByIdAsync(trace4.TraceId, CancellationToken.None);

        retrieved1.Should().BeNull(); // evicted!
        retrieved2.Should().NotBeNull();
        retrieved4.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByUserAsync_ReturnsOnlyTracesForSpecifiedUser()
    {
        var traceAlice = new AgentTrace { TraceId = Guid.NewGuid(), UserId = "alice", StartedAt = DateTimeOffset.UtcNow, Success = true };
        var traceBob = new AgentTrace { TraceId = Guid.NewGuid(), UserId = "bob", StartedAt = DateTimeOffset.UtcNow, Success = true };

        await _repo.AddAsync(traceAlice, CancellationToken.None);
        await _repo.AddAsync(traceBob, CancellationToken.None);

        var aliceTraces = await _repo.GetByUserAsync("alice", 10, CancellationToken.None);
        aliceTraces.Should().HaveCount(1);
        aliceTraces[0].UserId.Should().Be("alice");
    }

    [Fact]
    public async Task GetErrorsByTenantAsync_FiltersByCategoryAndSuccess()
    {
        var successTrace = new AgentTrace
        {
            TraceId = Guid.NewGuid(),
            UserId = "user-1",
            TenantId = "tenant-a",
            StartedAt = DateTimeOffset.UtcNow,
            Success = true
        };

        var dlpError = new AgentTrace
        {
            TraceId = Guid.NewGuid(),
            UserId = "user-1",
            TenantId = "tenant-a",
            StartedAt = DateTimeOffset.UtcNow,
            Success = false,
            ErrorCategory = AgentErrorCategory.DlpViolation,
            ErrorDetail = "SSN pattern detected"
        };

        var promptInjectionError = new AgentTrace
        {
            TraceId = Guid.NewGuid(),
            UserId = "user-2",
            TenantId = "tenant-a",
            StartedAt = DateTimeOffset.UtcNow,
            Success = false,
            ErrorCategory = AgentErrorCategory.PromptInjection,
            ErrorDetail = "System prompt override pattern detected"
        };

        await _repo.AddAsync(successTrace, CancellationToken.None);
        await _repo.AddAsync(dlpError, CancellationToken.None);
        await _repo.AddAsync(promptInjectionError, CancellationToken.None);

        var dlpErrors = await _repo.GetErrorsByTenantAsync("tenant-a", DateTimeOffset.UtcNow.AddHours(-1), "DlpViolation", 10, CancellationToken.None);
        dlpErrors.Should().HaveCount(1);
        dlpErrors[0].ErrorCategory.Should().Be(AgentErrorCategory.DlpViolation);

        var allErrors = await _repo.GetErrorsByTenantAsync("tenant-a", DateTimeOffset.UtcNow.AddHours(-1), null, 10, CancellationToken.None);
        allErrors.Should().HaveCount(2);
    }
}