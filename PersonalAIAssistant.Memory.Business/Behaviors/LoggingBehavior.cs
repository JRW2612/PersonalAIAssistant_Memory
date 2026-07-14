using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Business.Behaviors
{
    /// <summary>
    /// Logs every command/query with a correlation id and timing, and flags slow requests.
    /// </summary>
    public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
        private const int SlowRequestThresholdMs = 1000;

        public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        {
            _logger = logger;
        }
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            var requestName = typeof(TRequest).Name;
            var correlationId = Guid.NewGuid();
            var stopwatch=Stopwatch.StartNew();

            _logger.LogInformation("Handling {RequestName} with CorrelationId: {CorrelationId}", requestName, correlationId);
                       
            try
            {
                var response = await next(cancellationToken);
                stopwatch.Stop();

                if (stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs)
                {
                    _logger.LogWarning("Slow request detected: {RequestName} with CorrelationId: {CorrelationId} took {ElapsedMilliseconds} ms", requestName, correlationId, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogInformation("Handled {RequestName} with CorrelationId: {CorrelationId} in {ElapsedMilliseconds} ms", requestName, correlationId, stopwatch.ElapsedMilliseconds);
                }
                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error occurred while handling {RequestName} with CorrelationId: {CorrelationId}", requestName, correlationId);
                throw;
            }
        }
    }
}
