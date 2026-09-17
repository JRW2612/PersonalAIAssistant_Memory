// PersonalAIAssistant.Memory.Infrastructure/InMemory/InMemoryConversationBuffer.cs
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IConversationBuffer"/> for development,
/// testing, and standalone local environments.
/// </summary>
public class InMemoryConversationBuffer : IConversationBuffer
{
    private readonly ConcurrentDictionary<string, List<ConversationTurn>> _buffers = new();
    private readonly ConversationBufferOptions _options;
    private readonly ILogger<InMemoryConversationBuffer> _logger;

    public InMemoryConversationBuffer(
        IOptions<ConversationBufferOptions> options,
        ILogger<InMemoryConversationBuffer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private static string GetKey(string userId, string sessionId) => $"{userId}:{sessionId}";

    public Task AppendTurnAsync(string userId, string sessionId, string role, string content, CancellationToken ct = default)
    {
        var key = GetKey(userId, sessionId);
        var turn = new ConversationTurn(role, content, DateTime.UtcNow);

        _buffers.AddOrUpdate(key,
            _ => new List<ConversationTurn> { turn },
            (_, list) =>
            {
                lock (list)
                {
                    list.Add(turn);
                    // Keep cap within MaxTurns
                    if (list.Count > _options.MaxTurns)
                    {
                        var excess = list.Count - _options.MaxTurns;
                        list.RemoveRange(0, excess);
                    }
                }
                return list;
            });

        _logger.LogDebug("Appended conversation turn ({Role}) to buffer for user {UserId}, session {SessionId}",
            role, userId, sessionId);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConversationTurn>> GetRecentTurnsAsync(string userId, string sessionId, int maxTurns, CancellationToken ct = default)
    {
        var key = GetKey(userId, sessionId);
        if (!_buffers.TryGetValue(key, out var list))
        {
            return Task.FromResult<IReadOnlyList<ConversationTurn>>(Array.Empty<ConversationTurn>());
        }

        lock (list)
        {
            var takeCount = Math.Min(maxTurns, list.Count);
            var skipCount = list.Count - takeCount;
            IReadOnlyList<ConversationTurn> result = list.Skip(skipCount).Take(takeCount).ToList();
            return Task.FromResult(result);
        }
    }

    public Task<int> GetTurnCountAsync(string userId, string sessionId, CancellationToken ct = default)
    {
        var key = GetKey(userId, sessionId);
        if (!_buffers.TryGetValue(key, out var list))
        {
            return Task.FromResult(0);
        }

        lock (list)
        {
            return Task.FromResult(list.Count);
        }
    }

    public Task<IReadOnlyList<ConversationTurn>> FlushAndClearAsync(string userId, string sessionId, CancellationToken ct = default)
    {
        var key = GetKey(userId, sessionId);
        if (!_buffers.TryRemove(key, out var list))
        {
            return Task.FromResult<IReadOnlyList<ConversationTurn>>(Array.Empty<ConversationTurn>());
        }

        lock (list)
        {
            IReadOnlyList<ConversationTurn> copy = list.ToList();
            _logger.LogInformation("Flushed and cleared {Count} conversation turns for user {UserId}, session {SessionId}",
                copy.Count, userId, sessionId);
            return Task.FromResult(copy);
        }
    }

    public Task ClearAsync(string userId, string sessionId, CancellationToken ct = default)
    {
        var key = GetKey(userId, sessionId);
        _buffers.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
