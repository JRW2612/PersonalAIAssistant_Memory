using System.Reflection;

namespace PersonalAIAssistant.Memory.Core.Diagnostics;

/// <summary>
/// Deterministic binary isolation triage classifier for 504 Gateway Timeout incidents.
/// Accurately attributes failures to:
/// 1. Client-to-Gateway (network drops, slow upload, TLS handshake timeout)
/// 2. Gateway-to-Service (proxy timeouts, thread pool / connection pool exhaustion, backlog)
/// 3. Service-to-Database (deadlocks 40P01, statement timeouts 57014, lock waits 55P03, unindexed scans)
/// </summary>
public class Triage504Classifier : ITriage504Classifier
{
    public const double DefaultSlowDatabaseScanThresholdMs = 5000.0;
    public const double DefaultSlowClientUploadThresholdMs = 10000.0;

    public TimeoutTriageResult Classify(
        Exception? exception,
        HopTimingMetrics metrics,
        string? activeQueryTag = null,
        string? offendingCommand = null,
        bool clientAbortedBeforeDispatch = false)
    {
        // 1. Evaluate Hop 3: Service-to-Database Isolation
        // Check for explicit database exceptions, PostgreSQL error codes, or dominant DB duration
        var dbTriage = TryClassifyDatabaseFailure(exception, metrics, activeQueryTag, offendingCommand);
        if (dbTriage != null)
        {
            return dbTriage;
        }

        // 2. Evaluate Hop 1: Client-to-Gateway Isolation
        if (clientAbortedBeforeDispatch)
        {
            return new TimeoutTriageResult(
                TimeoutHop.ClientToGateway,
                TimeoutReason.ClientNetworkAbort,
                "Client connection aborted or reset before gateway could dispatch the request upstream.",
                metrics,
                Remediation: "Ensure client HTTP client timeout is greater than gateway proxy SLA and check client network stability."
            );
        }

        if (metrics.ClientToGatewayMs.HasValue && metrics.ClientToGatewayMs.Value >= DefaultSlowClientUploadThresholdMs)
        {
            return new TimeoutTriageResult(
                TimeoutHop.ClientToGateway,
                TimeoutReason.SlowClientUpload,
                $"Client request body upload stalled (elapsed {metrics.ClientToGatewayMs.Value:F1}ms) exceeding client upload budget.",
                metrics,
                Remediation: "Optimize payload compression or check client upstream bandwidth."
            );
        }

        var c2gReason = TryClassifyClientToGateway(exception);
        if (c2gReason.HasValue)
        {
            return new TimeoutTriageResult(
                TimeoutHop.ClientToGateway,
                c2gReason.Value,
                c2gReason.Value == TimeoutReason.TlsHandshakeTimeout
                    ? "TLS handshake negotiation between client and gateway timed out."
                    : "Client network communication terminated prematurely.",
                metrics,
                Remediation: "Verify client TLS configuration, cipher suites, and intermediate network latency."
            );
        }

        // 3. Evaluate Hop 2: Gateway-to-Service Isolation (Default / Remainder)
        // If neither the database nor the client connection was the root cause, the bottleneck
        // is the gateway-to-service hop (proxy timeout, service queue backlog, thread starvation).
        var g2sReason = TryClassifyGatewayToService(exception);
        var detail = g2sReason == TimeoutReason.ThreadPoolStarvation
            ? "Service or gateway thread pool starvation detected while waiting for service response."
            : g2sReason == TimeoutReason.ServiceQueueBacklog
                ? "Service request queue backlog caused proxy timeout before handler execution."
                : "Upstream service failed to respond within configured gateway proxy SLA.";

        return new TimeoutTriageResult(
            TimeoutHop.GatewayToService,
            g2sReason,
            detail,
            metrics,
            Remediation: "Inspect downstream service instance health, scale CPU/worker replicas, or check thread pool starvation."
        );
    }

    private static TimeoutTriageResult? TryClassifyDatabaseFailure(
        Exception? ex,
        HopTimingMetrics metrics,
        string? queryTag,
        string? offendingCommand)
    {
        if (ex != null)
        {
            var (isDbException, sqlState, message) = ExtractDbExceptionDetails(ex);

            if (isDbException || sqlState != null)
            {
                // Postgres 40P01 = deadlock_detected
                if (string.Equals(sqlState, "40P01", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("deadlock", StringComparison.OrdinalIgnoreCase))
                {
                    return new TimeoutTriageResult(
                        TimeoutHop.ServiceToDatabase,
                        TimeoutReason.DbDeadlock,
                        "Database transaction deadlock detected (PostgreSQL 40P01).",
                        metrics,
                        DbQueryTag: queryTag,
                        Remediation: "Re-order database statements within the transaction to acquire locks in consistent order, or lower transaction isolation.",
                        ErrorCode: "40P01",
                        OffendingCommand: offendingCommand
                    );
                }

                // Postgres 57014 = query_canceled (e.g. statement_timeout)
                if (string.Equals(sqlState, "57014", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("statement timeout", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("canceling statement due to statement timeout", StringComparison.OrdinalIgnoreCase))
                {
                    return new TimeoutTriageResult(
                        TimeoutHop.ServiceToDatabase,
                        TimeoutReason.DbQueryTimeout,
                        "Database query statement timeout exceeded (PostgreSQL 57014).",
                        metrics,
                        DbQueryTag: queryTag,
                        Remediation: !string.IsNullOrWhiteSpace(queryTag)
                            ? $"Analyze query plan (EXPLAIN ANALYZE) and add covering index for query tagged: '{queryTag}'."
                            : "Analyze query plan (EXPLAIN ANALYZE) and add missing database indices.",
                        ErrorCode: "57014",
                        OffendingCommand: offendingCommand
                    );
                }

                // Postgres 55P03 = lock_not_available
                if (string.Equals(sqlState, "55P03", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("lock_not_available", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("lock wait timeout", StringComparison.OrdinalIgnoreCase))
                {
                    return new TimeoutTriageResult(
                        TimeoutHop.ServiceToDatabase,
                        TimeoutReason.DbLockWait,
                        "Database lock acquisition timed out waiting for concurrent transaction (PostgreSQL 55P03).",
                        metrics,
                        DbQueryTag: queryTag,
                        Remediation: "Identify long-running uncommitted transactions holding conflicting row or table locks.",
                        ErrorCode: "55P03",
                        OffendingCommand: offendingCommand
                    );
                }

                // Connection pool exhaustion: 08001 / 08006 or message
                if (string.Equals(sqlState, "08001", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sqlState, "08006", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("connection pool", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("timeout waiting for a connection", StringComparison.OrdinalIgnoreCase))
                {
                    return new TimeoutTriageResult(
                        TimeoutHop.ServiceToDatabase,
                        TimeoutReason.DbConnectionPoolExhaustion,
                        "Database connection pool exhausted while acquiring connection.",
                        metrics,
                        DbQueryTag: queryTag,
                        Remediation: "Increase Maximum Pool Size in connection string or check for connection leaks in service repositories.",
                        ErrorCode: sqlState ?? "POOL_EXHAUSTED",
                        OffendingCommand: offendingCommand
                    );
                }

                // General DB Timeout
                if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                    ex is TimeoutException)
                {
                    return new TimeoutTriageResult(
                        TimeoutHop.ServiceToDatabase,
                        TimeoutReason.DbQueryTimeout,
                        "Database command execution timed out.",
                        metrics,
                        DbQueryTag: queryTag,
                        Remediation: !string.IsNullOrWhiteSpace(queryTag)
                            ? $"Review execution plan and index performance for database query tagged: '{queryTag}'."
                            : "Review database query execution plan and table scans.",
                        ErrorCode: sqlState,
                        OffendingCommand: offendingCommand
                    );
                }
            }
        }

        // Check if database query duration dominated the request
        if (metrics.ServiceToDatabaseMs.HasValue && metrics.ServiceToDatabaseMs.Value >= DefaultSlowDatabaseScanThresholdMs)
        {
            return new TimeoutTriageResult(
                TimeoutHop.ServiceToDatabase,
                TimeoutReason.UnindexedScanSuspected,
                $"Database query execution duration ({metrics.ServiceToDatabaseMs.Value:F1}ms) exceeded slow query threshold ({DefaultSlowDatabaseScanThresholdMs}ms). Unindexed table scan suspected.",
                metrics,
                DbQueryTag: queryTag,
                Remediation: !string.IsNullOrWhiteSpace(queryTag)
                    ? $"Create index on filtering/sorting columns for query tagged: '{queryTag}'."
                    : "Create missing index on filtering/sorting columns to eliminate sequential scan.",
                ErrorCode: "SLOW_DB_SCAN",
                OffendingCommand: offendingCommand
            );
        }

        return null;
    }

    private static TimeoutReason? TryClassifyClientToGateway(Exception? ex)
    {
        if (ex == null) return null;

        var fullMessage = $"{ex.GetType().Name} {ex.Message}";
        if (fullMessage.Contains("TLS", StringComparison.OrdinalIgnoreCase) ||
            fullMessage.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
            fullMessage.Contains("Handshake", StringComparison.OrdinalIgnoreCase))
        {
            return TimeoutReason.TlsHandshakeTimeout;
        }

        if (fullMessage.Contains("BadHttpRequestException", StringComparison.OrdinalIgnoreCase) ||
            fullMessage.Contains("Request body", StringComparison.OrdinalIgnoreCase) ||
            fullMessage.Contains("stalled", StringComparison.OrdinalIgnoreCase))
        {
            return TimeoutReason.SlowClientUpload;
        }

        if (fullMessage.Contains("ClientReset", StringComparison.OrdinalIgnoreCase) ||
            fullMessage.Contains("ConnectionReset", StringComparison.OrdinalIgnoreCase) ||
            fullMessage.Contains("ConnectionAborted", StringComparison.OrdinalIgnoreCase))
        {
            return TimeoutReason.ClientNetworkAbort;
        }

        return null;
    }

    private static TimeoutReason TryClassifyGatewayToService(Exception? ex)
    {
        if (ex == null) return TimeoutReason.ProxyTimeout;

        var msg = ex.Message.ToLowerInvariant();
        if (msg.Contains("threadpool") || msg.Contains("threads starved"))
            return TimeoutReason.ThreadPoolStarvation;

        if (msg.Contains("backlog") || msg.Contains("queue limit"))
            return TimeoutReason.ServiceQueueBacklog;

        if (msg.Contains("connection pool") || msg.Contains("pool exhausted"))
            return TimeoutReason.GatewayConnectionPoolExhaustion;

        return TimeoutReason.ProxyTimeout;
    }

    private static (bool IsDbException, string? SqlState, string Message) ExtractDbExceptionDetails(Exception ex)
    {
        // Recursively inspect inner exceptions (e.g. DbUpdateException -> PostgresException)
        Exception? current = ex;
        while (current != null)
        {
            var typeName = current.GetType().Name;
            var isDb = typeName.Contains("Db", StringComparison.OrdinalIgnoreCase) ||
                       typeName.Contains("Postgres", StringComparison.OrdinalIgnoreCase) ||
                       typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                       typeName.Contains("Sql", StringComparison.OrdinalIgnoreCase) ||
                       typeName.Contains("Mongo", StringComparison.OrdinalIgnoreCase);

            string? sqlState = null;
            // Check property 'SqlState' (common in Npgsql)
            var sqlStateProp = current.GetType().GetProperty("SqlState", BindingFlags.Public | BindingFlags.Instance);
            if (sqlStateProp != null && sqlStateProp.PropertyType == typeof(string))
            {
                sqlState = (string?)sqlStateProp.GetValue(current);
            }

            if (sqlState == null && current.Data.Contains("SqlState"))
            {
                sqlState = current.Data["SqlState"]?.ToString();
            }

            var hasDbMessageKeywords = current.Message.Contains("database", StringComparison.OrdinalIgnoreCase) ||
                                       current.Message.Contains("statement timeout", StringComparison.OrdinalIgnoreCase) ||
                                       current.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase) ||
                                       current.Message.Contains("lock_not_available", StringComparison.OrdinalIgnoreCase);

            if (isDb || sqlState != null || hasDbMessageKeywords)
            {
                return (true, sqlState, current.Message);
            }

            current = current.InnerException;
        }

        return (false, null, ex.Message);
    }
}
