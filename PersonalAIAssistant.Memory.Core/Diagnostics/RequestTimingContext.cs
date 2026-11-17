using System.Diagnostics;

namespace PersonalAIAssistant.Memory.Core.Diagnostics;

/// <summary>
/// Scoped timing accumulator tracking boundary hop timings and EF Core execution metrics.
/// Thread-safe for parallel child tasks within the same request scope.
/// </summary>
public class RequestTimingContext : IRequestTimingContext
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly object _lock = new();

    private double _dbMs = 0;
    private int _dbQueryCount = 0;

    public double? ClientToGatewayMs { get; set; }
    public double? GatewayToServiceMs { get; set; }
    public double ServiceToDatabaseMs
    {
        get
        {
            lock (_lock)
            {
                return _dbMs;
            }
        }
    }

    public string? LastExecutedQueryTag { get; set; }
    public string? LastExecutedCommandText { get; set; }
    public Exception? LastDatabaseException { get; set; }
    public int DatabaseQueryCount => _dbQueryCount;

    public void RecordDatabaseExecution(TimeSpan duration, string? queryTag, string? commandText)
    {
        Interlocked.Increment(ref _dbQueryCount);
        lock (_lock)
        {
            _dbMs += duration.TotalMilliseconds;
            if (!string.IsNullOrWhiteSpace(queryTag))
                LastExecutedQueryTag = queryTag;
            if (!string.IsNullOrWhiteSpace(commandText))
                LastExecutedCommandText = commandText;
        }
    }

    public void RecordDatabaseFailure(Exception ex, string? queryTag, string? commandText)
    {
        lock (_lock)
        {
            LastDatabaseException = ex;
            if (!string.IsNullOrWhiteSpace(queryTag))
                LastExecutedQueryTag = queryTag;
            if (!string.IsNullOrWhiteSpace(commandText))
                LastExecutedCommandText = commandText;
        }
    }

    public HopTimingMetrics GetMetrics()
    {
        double total;
        double dbMs;
        lock (_lock)
        {
            total = _stopwatch.Elapsed.TotalMilliseconds;
            dbMs = _dbMs;
        }

        return new HopTimingMetrics(ClientToGatewayMs, GatewayToServiceMs, dbMs > 0 ? dbMs : null, total);
    }
}
