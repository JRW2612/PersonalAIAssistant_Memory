using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using PersonalAIAssistant.Memory.Core.Interfaces.Common;

namespace PersonalAIAssistant.Memory.Business.Behaviors
{
    /// <summary>
    /// Logs every command/query with a correlation ID and elapsed time, and flags slow requests.
    /// The correlation ID is taken from the request itself when it implements
    /// <see cref="ICorrelatedRequest"/>; otherwise a fresh ID is generated so every request
    /// is always traceable.
    /// </summary>
    public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
        private const int SlowRequestThresholdMs = 1000;

        public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        {
            _logger = logger;
        }

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            var requestName = typeof(TRequest).Name;

            var correlationId = request is ICorrelatedRequest correlated && correlated.CorrelationId is not null
                ? correlated.CorrelationId
                : Guid.NewGuid().ToString();

            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation(
                "Handling {RequestName} [CorrelationId={CorrelationId}]",
                requestName, correlationId);

            try
            {
                var response = await next(cancellationToken);
                stopwatch.Stop();

                if (stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs)
                {
                    _logger.LogWarning(
                        "Slow request: {RequestName} took {ElapsedMs} ms [CorrelationId={CorrelationId}]",
                        requestName, stopwatch.ElapsedMilliseconds, correlationId);
                }
                else
                {
                    _logger.LogInformation(
                        "Handled {RequestName} in {ElapsedMs} ms [CorrelationId={CorrelationId}]",
                        requestName, stopwatch.ElapsedMilliseconds, correlationId);
                }

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "Error handling {RequestName} after {ElapsedMs} ms [CorrelationId={CorrelationId}]",
                    requestName, stopwatch.ElapsedMilliseconds, correlationId);
                throw;
            }
        }
    }
}
