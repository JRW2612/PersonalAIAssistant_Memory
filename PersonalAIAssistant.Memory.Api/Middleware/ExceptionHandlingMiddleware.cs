using System.Net;
using System.Text.Json;
using FluentValidation;
using PersonalAIAssistant.Memory.Core.Diagnostics;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Observability;

namespace PersonalAIAssistant.Memory.Api.Middleware;

/// <summary>
/// Global exception handling middleware that maps application and domain exceptions
/// to standard HTTP status codes (400 Bad Request, 404 Not Found, 403 Forbidden, 504 Gateway Timeout, etc.)
/// and performs systematic 504 triage with RFC 7807 ProblemDetails and diagnostic headers.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly ITriage504Classifier _classifier;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        ITriage504Classifier? classifier = null)
    {
        _next = next;
        _logger = logger;
        _classifier = classifier ?? new Triage504Classifier();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation error occurred during request execution.");

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

            var errors = ex.Errors.Select(e => new
            {
                Field = e.PropertyName,
                Error = e.ErrorMessage
            });

            var response = new
            {
                Status = (int)HttpStatusCode.BadRequest,
                Title = "Validation Failed",
                Errors = errors
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (PersonalAIAssistant.Memory.Core.Exceptions.DlpViolationException dlpEx)
        {
            _logger.LogWarning(dlpEx, "DLP Policy Violation: {Message}", dlpEx.Message);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                Status = 400,
                Title = "DLP Policy Violation",
                Detail = dlpEx.Message,
                Violations = dlpEx.Violations.Select(v => new { Category = v.Category.ToString(), v.Description })
            });
            return;
        }
        catch (PersonalAIAssistant.Memory.Core.Exceptions.PromptInjectionException piEx)
        {
            _logger.LogWarning(piEx, "Prompt Injection Detected: Category={Category} | Message={Message}", piEx.Category, piEx.Message);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                Status = 400,
                Title = "Prompt Injection Detected",
                Detail = piEx.Message,
                Category = piEx.Category.ToString(),
                piEx.MatchedPattern
            });
            return;
        }
        catch (PersonalAIAssistant.Memory.Core.Exceptions.InputSanitizationException isEx)
        {
            _logger.LogWarning(isEx, "Input Sanitization Rejected: ViolationType={Type} | Message={Message}", isEx.ViolationType, isEx.Message);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                Status = 400,
                Title = "Input Sanitization Rejected",
                Detail = isEx.Message,
                isEx.ViolationType
            });
            return;
        }
        catch (PersonalAIAssistant.Memory.Core.Exceptions.SsrfSecurityException ssrfEx)
        {
            _logger.LogWarning(ssrfEx, "SSRF Policy Violation: TargetUrl={Url} | Reason={Reason}", ssrfEx.TargetUrl, ssrfEx.Reason);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                Status = 400,
                Title = "SSRF Policy Violation",
                Detail = ssrfEx.Message,
                ssrfEx.TargetUrl,
                ssrfEx.Reason
            });
            return;
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain exception occurred: {Message}", ex.Message);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

            var response = new
            {
                Status = (int)HttpStatusCode.BadRequest,
                Title = "Bad Request",
                Detail = ex.Message
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found: {Message}", ex.Message);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;

            var response = new
            {
                Status = (int)HttpStatusCode.NotFound,
                Title = "Not Found",
                Detail = ex.Message
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt: {Message}", ex.Message);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;

            var response = new
            {
                Status = (int)HttpStatusCode.Forbidden,
                Title = "Forbidden",
                Detail = ex.Message
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            var timing = context.RequestServices.GetService<IRequestTimingContext>();
            var metrics = timing?.GetMetrics() ?? new HopTimingMetrics(null, null, null, 0);

            if (IsTimeoutOrDatabaseFailure(ex, metrics))
            {
                var queryTag = timing?.LastExecutedQueryTag;
                var commandText = timing?.LastExecutedCommandText;
                var clientAborted = context.RequestAborted.IsCancellationRequested;

                var triage = _classifier.Classify(ex, metrics, queryTag, commandText, clientAborted);
                await Handle504TriageAsync(context, triage);
                return;
            }

            _logger.LogError(ex, "An unhandled server exception occurred.");

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = new
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred processing your request."
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }

    private async Task Handle504TriageAsync(HttpContext context, TimeoutTriageResult triage)
    {
        _logger.LogWarning(
            "HTTP 504 Gateway Timeout: Phase={TimeoutPhase} Reason={TimeoutReason} QueryTag={DbQueryTag} ServerTiming={ServerTiming}",
            triage.Hop,
            triage.Reason,
            triage.DbQueryTag ?? "None",
            triage.Metrics.ToServerTimingHeader());

        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            context.Response.ContentType = "application/problem+json";

            var traceparent = context.Items["Traceparent"]?.ToString()
                           ?? context.Response.Headers[W3CTraceContext.HeaderName].FirstOrDefault()
                           ?? W3CTraceContext.GetOrCreate(context.Request.Headers[W3CTraceContext.HeaderName].FirstOrDefault()).ToString();

            context.Response.Headers[W3CTraceContext.HeaderName] = traceparent;
            context.Response.Headers["X-Timeout-Phase"] = triage.Hop.ToString();
            context.Response.Headers["X-Timeout-Reason"] = triage.Reason.ToString();

            var serverTiming = triage.Metrics.ToServerTimingHeader();
            if (!string.IsNullOrWhiteSpace(serverTiming))
            {
                context.Response.Headers["Server-Timing"] = serverTiming;
            }

            var problem = new Triage504ProblemDetails
            {
                Status = StatusCodes.Status504GatewayTimeout,
                Title = "Gateway Timeout",
                Detail = triage.Detail,
                Traceparent = traceparent,
                TimeoutPhase = triage.Hop.ToString(),
                TimeoutReason = triage.Reason.ToString(),
                HopMetrics = triage.Metrics,
                Diagnostics = new TriageDiagnosticsInfo(
                    triage.DbQueryTag,
                    triage.ErrorCode,
                    triage.Remediation,
                    triage.OffendingCommand)
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    }

    private static bool IsTimeoutOrDatabaseFailure(Exception ex, HopTimingMetrics metrics)
    {
        if (ex is TimeoutException or OperationCanceledException or TaskCanceledException)
            return true;

        if (metrics.ServiceToDatabaseMs.HasValue && metrics.ServiceToDatabaseMs.Value >= Triage504Classifier.DefaultSlowDatabaseScanThresholdMs)
            return true;

        var cur = ex;
        while (cur != null)
        {
            var msg = cur.Message;
            if (msg.Contains("deadlock", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("40P01", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("statement timeout", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("57014", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("lock_not_available", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("55P03", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("connection pool", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            cur = cur.InnerException;
        }

        return false;
    }
}
