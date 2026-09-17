using System.Diagnostics;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Common;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Behaviors;

/// <summary>
/// MediatR pipeline behavior that captures a structured <see cref="AgentTrace"/> for every
/// request that passes through the pipeline.
/// <para>
/// Responsibilities:
/// <list type="bullet">
///   <item>Records the StartedAt timestamp and ambient OpenTelemetry trace ID.</item>
///   <item>Accumulates <see cref="BehaviorStep"/> timings from other behaviors (via the shared trace context).</item>
///   <item>On success: marks the trace Success=true and persists it.</item>
///   <item>On failure: classifies the exception into <see cref="AgentErrorCategory"/> and persists the trace.</item>
///   <item>Emits an Activity tag <c>agent.trace_id</c> so the trace links to the OTLP span.</item>
/// </list>
/// </para>
/// </summary>
public sealed class AgentTracingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly JsonSerializerOptions SafeJson = new()
    {
        WriteIndented = false,
        MaxDepth = 8,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private const int MaxOutputJsonBytes = 4096;

    private readonly IAgentTraceRepository _traces;
    private readonly IUserContext _userContext;
    private readonly AgentTracingOptions _opts;
    private readonly ILogger<AgentTracingBehavior<TRequest, TResponse>> _logger;

    public AgentTracingBehavior(
        IAgentTraceRepository traces,
        IUserContext userContext,
        IOptions<AgentTracingOptions> opts,
        ILogger<AgentTracingBehavior<TRequest, TResponse>> logger)
    {
        _traces = traces;
        _userContext = userContext;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_opts.Enabled)
        {
            return await next(cancellationToken);
        }

        var requestName = typeof(TRequest).Name;
        var correlationId = request is ICorrelatedRequest corr ? corr.CorrelationId : null;
        var otelTraceId = Activity.Current?.TraceId.ToString();

        var trace = new AgentTrace
        {
            UserId = _userContext.UserId ?? "anonymous",
            TenantId = _userContext.TenantId,
            CorrelationId = correlationId,
            RequestName = requestName,
            StartedAt = DateTimeOffset.UtcNow,
            OtelTraceId = otelTraceId,
            InputJson = SerializeSafe(request)
        };

        // Tag the current OTEL span so this trace_id is queryable in the APM backend
        Activity.Current?.SetTag("agent.trace_id", trace.TraceId.ToString());
        Activity.Current?.SetTag("agent.request_name", requestName);
        Activity.Current?.SetTag("agent.user_id", trace.UserId);

        try
        {
            var response = await next(cancellationToken);

            trace.Success = true;
            trace.CompletedAt = DateTimeOffset.UtcNow;
            trace.OutputJson = SerializeSafe(response, MaxOutputJsonBytes);

            await _traces.AddAsync(trace, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            trace.Success = false;
            trace.CompletedAt = DateTimeOffset.UtcNow;
            trace.ErrorCategory = Classify(ex);
            trace.ErrorDetail = ex.Message;

            Activity.Current?.SetTag("agent.error_category", trace.ErrorCategory.ToString());
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogWarning(
                "AgentTracingBehavior: {RequestName} failed in {DurationMs} ms — Category={Category} | CorrelationId={CorrelationId}",
                requestName, trace.DurationMs, trace.ErrorCategory, correlationId);

            try
            {
                await _traces.AddAsync(trace, cancellationToken);
            }
            catch (Exception persistEx)
            {
                _logger.LogError(persistEx, "Failed to persist agent trace for {RequestName}", requestName);
            }

            throw;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AgentErrorCategory Classify(Exception ex) => ex switch
    {
        ValidationException         => AgentErrorCategory.ValidationFailure,
        PromptInjectionException    => AgentErrorCategory.PromptInjection,
        DlpViolationException       => AgentErrorCategory.DlpViolation,
        SsrfSecurityException       => AgentErrorCategory.SsrfViolation,
        InputSanitizationException  => AgentErrorCategory.InputSanitization,
        OperationCanceledException  => AgentErrorCategory.Timeout,
        UnauthorizedAccessException => AgentErrorCategory.Unauthorized,
        KeyNotFoundException        => AgentErrorCategory.NotFound,
        ConcurrencyException        => AgentErrorCategory.Concurrency,
        HttpRequestException        => AgentErrorCategory.AiProviderError,
        InvalidOperationException ioe when ioe.Message.Contains("AI", StringComparison.OrdinalIgnoreCase) ||
                                           ioe.Message.Contains("provider", StringComparison.OrdinalIgnoreCase)
                                    => AgentErrorCategory.AiProviderError,
        _                           => AgentErrorCategory.Unknown
    };

    private static string? SerializeSafe(object? obj, int maxBytes = int.MaxValue)
    {
        if (obj is null) return null;
        try
        {
            var json = JsonSerializer.Serialize(obj, SafeJson);
            return json.Length > maxBytes ? json[..maxBytes] + "…" : json;
        }
        catch
        {
            return null;
        }
    }
}