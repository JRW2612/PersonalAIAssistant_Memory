using System.Diagnostics;
using PersonalAIAssistant.Memory.Core.Diagnostics;
using PersonalAIAssistant.Memory.Core.Observability;

namespace PersonalAIAssistant.Memory.Api.Middleware;

/// <summary>
/// Middleware that enforces W3C Trace Context (traceparent) standard across all incoming HTTP requests.
/// Validates incoming traceparent headers or generates fresh W3C-compliant headers, attaches them to
/// the response, establishes distributed tracing ActivityContext, and tracks boundary hop timings.
/// </summary>
public class TraceContextMiddleware
{
    private static readonly ActivitySource ActivitySource = new("PersonalAIAssistant.Memory.Api");
    private readonly RequestDelegate _next;
    private readonly ILogger<TraceContextMiddleware> _logger;

    public TraceContextMiddleware(RequestDelegate next, ILogger<TraceContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IRequestTimingContext timingContext)
    {
        var rawTraceparent = context.Request.Headers[W3CTraceContext.HeaderName].FirstOrDefault();
        var traceContext = W3CTraceContext.GetOrCreate(rawTraceparent);

        // Store resolved traceparent in HttpContext items and Response headers early
        context.Items["Traceparent"] = traceContext.ToString();
        context.Response.Headers[W3CTraceContext.HeaderName] = traceContext.ToString();

        // Extract client-to-gateway duration if supplied by gateway upstream header
        if (context.Request.Headers.TryGetValue("X-Client-Duration-Ms", out var c2gHeader) &&
            double.TryParse(c2gHeader.FirstOrDefault(), out var c2gMs))
        {
            timingContext.ClientToGatewayMs = c2gMs;
        }

        // Establish W3C Activity context
        using var activity = ActivitySource.StartActivity(
            $"{context.Request.Method} {context.Request.Path}",
            ActivityKind.Server,
            ActivityContext.Parse(traceContext.ToString(), null));

        var requestStartTime = Stopwatch.GetTimestamp();

        // Register Server-Timing header before response headers are sent
        context.Response.OnStarting(() =>
        {
            var elapsedMs = Stopwatch.GetElapsedTime(requestStartTime).TotalMilliseconds;
            timingContext.GatewayToServiceMs = elapsedMs;

            var metrics = timingContext.GetMetrics();
            var serverTiming = metrics.ToServerTimingHeader();
            if (!string.IsNullOrWhiteSpace(serverTiming) && !context.Response.Headers.ContainsKey("Server-Timing"))
            {
                context.Response.Headers["Server-Timing"] = serverTiming;
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
