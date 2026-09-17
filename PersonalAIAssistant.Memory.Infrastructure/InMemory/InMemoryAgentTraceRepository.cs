using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.InMemory
{
    /// <summary>
    /// Bounded in-memory ring-buffer implementation of <see cref="IAgentTraceRepository"/>.
    /// When <see cref="AgentTracingOptions.MaxTraces"/> is exceeded the oldest entry is evicted (FIFO).
    /// Thread-safe for concurrent write/read access.
    /// </summary>
    public sealed class InMemoryAgentTraceRepository : IAgentTraceRepository
    {
        private readonly AgentTracingOptions _opts;
        private readonly ILogger<InMemoryAgentTraceRepository> _logger;

        // Primary store indexed by TraceId
        private readonly ConcurrentDictionary<Guid, AgentTrace> _store = new();

        // Insertion-order queue for FIFO eviction
        private readonly ConcurrentQueue<Guid> _insertionOrder = new();

        public InMemoryAgentTraceRepository(
            IOptions<AgentTracingOptions> opts,
            ILogger<InMemoryAgentTraceRepository> logger)
        {
            _opts = opts.Value;
            _logger = logger;
        }

        public Task AddAsync(AgentTrace trace, CancellationToken ct)
        {
            // Evict oldest entries when at capacity
            while (_store.Count >= _opts.MaxTraces)
            {
                if (_insertionOrder.TryDequeue(out var evictId))
                {
                    _store.TryRemove(evictId, out _);
                }
                else
                {
                    break; // queue exhausted — should not happen
                }
            }

            if (_store.TryAdd(trace.TraceId, trace))
            {
                _insertionOrder.Enqueue(trace.TraceId);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentTrace>> GetByUserAsync(
            string userId,
            int limit,
            CancellationToken ct)
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-_opts.RetentionHours);

            var results = _store.Values
                .Where(t => t.UserId == userId && t.StartedAt >= cutoff)
                .OrderByDescending(t => t.StartedAt)
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<AgentTrace>>(results);
        }

        public Task<AgentTrace?> GetByIdAsync(Guid traceId, CancellationToken ct)
        {
            _store.TryGetValue(traceId, out var trace);
            return Task.FromResult(trace);
        }

        public Task<IReadOnlyList<AgentTrace>> GetErrorsByTenantAsync(
            string tenantId,
            DateTimeOffset since,
            string? errorCategory,
            int limit,
            CancellationToken ct)
        {
            var query = _store.Values
                .Where(t => t.TenantId == tenantId && !t.Success && t.StartedAt >= since);

            if (!string.IsNullOrWhiteSpace(errorCategory) &&
                Enum.TryParse<AgentErrorCategory>(errorCategory, ignoreCase: true, out var parsed))
            {
                query = query.Where(t => t.ErrorCategory == parsed);
            }

            var results = query
                .OrderByDescending(t => t.StartedAt)
                .Take(limit)
                .ToList();

            return Task.FromResult<IReadOnlyList<AgentTrace>>(results);
        }

        /// <summary>
        /// Called by a periodic cleanup job to purge traces older than RetentionHours.
        /// This is optional — the repository naturally shrinks at capacity, but explicit
        /// cleanup prevents stale data accumulating when write volume is low.
        /// </summary>
        public void PurgeExpired()
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-_opts.RetentionHours);
            var expired = _store.Values
                .Where(t => t.StartedAt < cutoff)
                .Select(t => t.TraceId)
                .ToList();

            foreach (var id in expired)
            {
                _store.TryRemove(id, out _);
            }

            if (expired.Count > 0)
            {
                _logger.LogDebug("AgentTrace retention purge removed {Count} expired entries.", expired.Count);
            }
        }
    }
}
