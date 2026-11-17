using System.Net;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalAIAssistant.Memory.Api.Middleware;
using PersonalAIAssistant.Memory.Core.Diagnostics;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Core.Observability;
using PersonalAIAssistant.Memory.Events;
using PersonalAIAssistant.Memory.Infrastructure.Events;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure;

public class Triage504AndTraceparentTests
{
    private readonly Triage504Classifier _classifier = new();

    #region W3C Trace Context Tests

    [Fact]
    public void W3CTraceContext_Create_GeneratesValidW3CHeader()
    {
        var trace = W3CTraceContext.Create(sampled: true);

        Assert.NotNull(trace);
        Assert.Equal("00", trace.Version);
        Assert.Equal(32, trace.TraceId.Length);
        Assert.Equal(16, trace.SpanId.Length);
        Assert.Equal("01", trace.Flags);
        Assert.True(trace.IsSampled);

        var header = trace.ToString();
        Assert.Equal(55, header.Length);
        Assert.Matches(@"^00-[0-9a-f]{32}-[0-9a-f]{16}-01$", header);
    }

    [Theory]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", true)]
    [InlineData("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00", true)]
    [InlineData("00-00000000000000000000000000000000-00f067aa0ba902b7-01", false)] // All-zero trace ID invalid
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-0000000000000000-01", false)] // All-zero span ID invalid
    [InlineData("01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", false)] // Unsupported version
    [InlineData("invalid-header-string", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void W3CTraceContext_TryParse_ValidatesAccordingToW3CSpec(string? header, bool expectedValid)
    {
        var success = W3CTraceContext.TryParse(header, out var context);

        Assert.Equal(expectedValid, success);
        if (expectedValid)
        {
            Assert.NotNull(context);
            Assert.Equal(header!.Trim(), context!.ToString());
        }
        else
        {
            Assert.Null(context);
        }
    }

    [Fact]
    public void W3CTraceContext_CreateChildSpan_PreservesTraceIdAndCreatesNewSpanId()
    {
        var parent = W3CTraceContext.Parse("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        var child = parent.CreateChildSpan();

        Assert.Equal(parent.TraceId, child.TraceId);
        Assert.NotEqual(parent.SpanId, child.SpanId);
        Assert.Equal(parent.Flags, child.Flags);
        Assert.Equal(parent.Version, child.Version);
    }

    #endregion

    #region 504 Systematic Triage Binary Isolation Tests

    [Fact]
    public void Triage504_ServiceToDatabase_IsolatesPostgresDeadlock40P01()
    {
        var dbEx = new Exception("PostgresException: 40P01: deadlock detected");
        dbEx.Data["SqlState"] = "40P01";
        var metrics = new HopTimingMetrics(10, 200, 1500, 1710);

        var result = _classifier.Classify(dbEx, metrics, activeQueryTag: "UserAction: ConsolidateMemories", offendingCommand: "SELECT ... FOR UPDATE");

        Assert.Equal(TimeoutHop.ServiceToDatabase, result.Hop);
        Assert.Equal(TimeoutReason.DbDeadlock, result.Reason);
        Assert.Equal("40P01", result.ErrorCode);
        Assert.Equal("UserAction: ConsolidateMemories", result.DbQueryTag);
        Assert.Contains("deadlock", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transaction", result.Remediation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Triage504_ServiceToDatabase_IsolatesStatementTimeout57014()
    {
        var dbEx = new Exception("canceling statement due to statement timeout (57014)");
        dbEx.Data["SqlState"] = "57014";
        var metrics = new HopTimingMetrics(15, 300, 30001, 30316);

        var result = _classifier.Classify(dbEx, metrics, activeQueryTag: "UserAction: SearchMemories | Tenant: corp", offendingCommand: "SELECT * FROM memories");

        Assert.Equal(TimeoutHop.ServiceToDatabase, result.Hop);
        Assert.Equal(TimeoutReason.DbQueryTimeout, result.Reason);
        Assert.Equal("57014", result.ErrorCode);
        Assert.Equal("UserAction: SearchMemories | Tenant: corp", result.DbQueryTag);
        Assert.Contains("index", result.Remediation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Triage504_ServiceToDatabase_IsolatesLockWait55P03()
    {
        var dbEx = new Exception("lock_not_available (55P03): could not obtain lock on table");
        dbEx.Data["SqlState"] = "55P03";
        var metrics = new HopTimingMetrics(5, 50, 12000, 12055);

        var result = _classifier.Classify(dbEx, metrics, activeQueryTag: "UserAction: LegalHold");

        Assert.Equal(TimeoutHop.ServiceToDatabase, result.Hop);
        Assert.Equal(TimeoutReason.DbLockWait, result.Reason);
        Assert.Equal("55P03", result.ErrorCode);
        Assert.Contains("lock", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Triage504_ServiceToDatabase_IsolatesUnindexedScanExceedingThreshold()
    {
        var metrics = new HopTimingMetrics(20, 100, 6500.0, 6620.0);

        var result = _classifier.Classify(null, metrics, activeQueryTag: "UserAction: FullTextScan");

        Assert.Equal(TimeoutHop.ServiceToDatabase, result.Hop);
        Assert.Equal(TimeoutReason.UnindexedScanSuspected, result.Reason);
        Assert.Equal("SLOW_DB_SCAN", result.ErrorCode);
        Assert.Contains("6500.0ms", result.Detail);
        Assert.Contains("index", result.Remediation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Triage504_ClientToGateway_IsolatesClientAbort()
    {
        var metrics = new HopTimingMetrics(50, null, null, 50);

        var result = _classifier.Classify(
            new OperationCanceledException("The operation was canceled"),
            metrics,
            clientAbortedBeforeDispatch: true);

        Assert.Equal(TimeoutHop.ClientToGateway, result.Hop);
        Assert.Equal(TimeoutReason.ClientNetworkAbort, result.Reason);
        Assert.Contains("Client connection aborted", result.Detail);
    }

    [Fact]
    public void Triage504_ClientToGateway_IsolatesSlowClientUpload()
    {
        var metrics = new HopTimingMetrics(15000.0, null, null, 15000.0);

        var result = _classifier.Classify(null, metrics);

        Assert.Equal(TimeoutHop.ClientToGateway, result.Hop);
        Assert.Equal(TimeoutReason.SlowClientUpload, result.Reason);
        Assert.Contains("stalled", result.Detail);
    }

    [Fact]
    public void Triage504_GatewayToService_IsolatesProxyTimeout()
    {
        var proxyEx = new TimeoutException("Upstream proxy request timed out");
        var metrics = new HopTimingMetrics(10, 30005, null, 30015);

        var result = _classifier.Classify(proxyEx, metrics);

        Assert.Equal(TimeoutHop.GatewayToService, result.Hop);
        Assert.Equal(TimeoutReason.ProxyTimeout, result.Reason);
        Assert.Contains("proxy SLA", result.Detail);
    }

    [Fact]
    public void Triage504_GatewayToService_IsolatesThreadPoolStarvation()
    {
        var threadEx = new Exception("System.InvalidOperationException: ThreadPool starvation detected");
        var metrics = new HopTimingMetrics(10, 15000, null, 15010);

        var result = _classifier.Classify(threadEx, metrics);

        Assert.Equal(TimeoutHop.GatewayToService, result.Hop);
        Assert.Equal(TimeoutReason.ThreadPoolStarvation, result.Reason);
        Assert.Contains("thread pool starvation", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Middleware & HTTP Contract Tests

    [Fact]
    public async Task TraceContextMiddleware_PropagatesIncomingTraceparentAndSetsServerTiming()
    {
        var incomingTraceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var context = new DefaultHttpContext();
        context.Request.Headers[W3CTraceContext.HeaderName] = incomingTraceparent;
        context.Request.Headers["X-Client-Duration-Ms"] = "18.5";

        var timingContext = new RequestTimingContext();
        var loggerMock = new Mock<ILogger<TraceContextMiddleware>>();

        var middleware = new TraceContextMiddleware(
            next: ctx =>
            {
                Assert.Equal(incomingTraceparent, ctx.Items["Traceparent"]);
                timingContext.RecordDatabaseExecution(TimeSpan.FromMilliseconds(42.3), "UserAction: Query", "SELECT 1");
                return Task.CompletedTask;
            },
            logger: loggerMock.Object);

        await middleware.InvokeAsync(context, timingContext);

        Assert.Equal(incomingTraceparent, context.Response.Headers[W3CTraceContext.HeaderName].ToString());
        Assert.Equal(18.5, timingContext.ClientToGatewayMs);
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_Renders504ProblemDetails_WithBinaryTriageDiagnostics()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var timingContext = new RequestTimingContext();
        timingContext.ClientToGatewayMs = 12.0;
        timingContext.GatewayToServiceMs = 50.0;
        timingContext.RecordDatabaseExecution(TimeSpan.FromMilliseconds(150.0), "UserAction: ScanMemories", "SELECT * FROM memories");

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IRequestTimingContext>(timingContext);
        context.RequestServices = serviceCollection.BuildServiceProvider();

        var loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();

        var middleware = new ExceptionHandlingMiddleware(
            next: _ => throw new TimeoutException("Database command execution timed out"),
            logger: loggerMock.Object,
            classifier: _classifier);

        await middleware.InvokeAsync(context);

        Assert.Equal((int)HttpStatusCode.GatewayTimeout, context.Response.StatusCode);
        Assert.Equal("ServiceToDatabase", context.Response.Headers["X-Timeout-Phase"].ToString());
        Assert.Equal("DbQueryTimeout", context.Response.Headers["X-Timeout-Reason"].ToString());
        Assert.True(context.Response.Headers.ContainsKey(W3CTraceContext.HeaderName));
        Assert.True(context.Response.Headers.ContainsKey("Server-Timing"));

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseJson = await reader.ReadToEndAsync();
        var problem = JsonSerializer.Deserialize<Triage504ProblemDetails>(responseJson);

        Assert.NotNull(problem);
        Assert.Equal(504, problem!.Status);
        Assert.Equal("ServiceToDatabase", problem.TimeoutPhase);
        Assert.Equal("DbQueryTimeout", problem.TimeoutReason);
        Assert.Equal("UserAction: ScanMemories", problem.Diagnostics.DbQueryTag);
        Assert.NotNull(problem.Diagnostics.Remediation);
    }

    #endregion

    #region Message Bus Header Propagation Tests

    [Fact]
    public async Task RabbitMQEventBus_EnrichesMemoryEvent_WithW3CTraceparentHeader()
    {
        var publishEndpointMock = new Mock<IPublishEndpoint>();

        publishEndpointMock
            .Setup(p => p.Publish<MemoryEvent>(It.IsAny<MemoryEvent>(), It.IsAny<IPipe<PublishContext<MemoryEvent>>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var bus = new RabbitMQEventBus(publishEndpointMock.Object);
        var testEvent = new MemoryAddedEvent { AggregateId = Guid.NewGuid() };

        await bus.PublishAsync(testEvent);

        Assert.NotNull(testEvent.Traceparent);
        Assert.Matches(@"^00-[0-9a-f]{32}-[0-9a-f]{16}-01$", testEvent.Traceparent);
    }

    private delegate bool TryGetHeaderDelegate(string key, out object? value);

    [Fact]
    public async Task MemoryEventConsumer_ExtractsW3CTraceparent_FromContextHeaders()
    {
        var incomingTraceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var consumeContextMock = new Mock<ConsumeContext<MemoryAddedEvent>>();
        var headersMock = new Mock<Headers>();

        headersMock
            .Setup(h => h.TryGetHeader(W3CTraceContext.HeaderName, out It.Ref<object?>.IsAny))
            .Returns(new TryGetHeaderDelegate((string key, out object? val) =>
            {
                val = incomingTraceparent;
                return true;
            }));

        consumeContextMock.Setup(c => c.Headers).Returns(headersMock.Object);

        var testEvent = new MemoryAddedEvent
        {
            AggregateId = Guid.NewGuid(),
            Traceparent = incomingTraceparent
        };
        consumeContextMock.Setup(c => c.Message).Returns(testEvent);

        var handlerMock = new Mock<IMemoryEventHandler<MemoryAddedEvent>>();
        handlerMock.Setup(h => h.HandleAsync(It.IsAny<MemoryAddedEvent>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddScoped(_ => handlerMock.Object);
        var sp = services.BuildServiceProvider();

        var loggerMock = new Mock<ILogger<MemoryEventConsumer<MemoryAddedEvent>>>();
        var consumer = new MemoryEventConsumer<MemoryAddedEvent>(sp, loggerMock.Object);

        await consumer.Consume(consumeContextMock.Object);

        Assert.Equal(incomingTraceparent, testEvent.Traceparent);
        handlerMock.Verify(h => h.HandleAsync(It.Is<MemoryAddedEvent>(e => e.Traceparent == incomingTraceparent), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
