using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Business.Behaviors
{
    /// <summary>
    /// MediatR pipeline behavior enforcing corporate DLP policy on memory write commands.
    /// SRP: only handles DLP scanning. OCP: new commands opt-in via IDlpScannableRequest.
    /// </summary>
    public sealed class DataLossPreventionBehavior<TRequest, TResponse>
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        private readonly IDataLossPreventionService _dlp;
        private readonly DlpOptions _opts;
        private readonly ILogger<DataLossPreventionBehavior<TRequest, TResponse>> _logger;

        public DataLossPreventionBehavior(
            IDataLossPreventionService dlp,
            IOptions<DlpOptions> opts,
            ILogger<DataLossPreventionBehavior<TRequest, TResponse>> logger)
        {
            _dlp = dlp;
            _opts = opts.Value;
            _logger = logger;
        }

        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            if (!_opts.Enabled)
                return await next(cancellationToken);

            var textToScan = ExtractText(request);
            if (textToScan is null)
                return await next(cancellationToken);

            var result = _dlp.Scan(textToScan);

            if (result.HasViolations && _opts.BlockOnViolation)
            {
                _logger.LogWarning(
                    "[DLP] Blocking {RequestType} for UserId {UserId} — {Count} violation(s) detected.",
                    typeof(TRequest).Name,
                    ExtractUserId(request),
                    result.Violations.Count);

                throw new DlpViolationException(result.Violations);
            }

            return await next(cancellationToken);
        }

        private static string? ExtractText(TRequest request) => request switch
        {
            AddMemoryCommand cmd => cmd.RawText,
            UpdateMemoryCommand cmd => cmd.UpdatedFields?.GetValueOrDefault("RawText"),
            ConsolidateMemoriesCommand cmd => cmd.ConsolidatedText,
            _ => null
        };

        private static string ExtractUserId(TRequest request) => request switch
        {
            AddMemoryCommand cmd => cmd.UserId,
            UpdateMemoryCommand cmd => cmd.UserId,
            ConsolidateMemoriesCommand cmd => cmd.UserId,
            _ => "unknown"
        };
    }
}
