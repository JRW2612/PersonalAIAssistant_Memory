// PersonalAIAssistant.Memory.Infrastructure/Context/RedisConversationBuffer.cs
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Models;
using StackExchange.Redis;

namespace PersonalAIAssistant.Memory.Infrastructure.Context;

/// <summary>
/// Distributed, persistent short-term conversation buffer using Redis lists with sliding TTL expiry.
/// </summary>
public class RedisConversationBuffer : IConversationBuffer
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ConversationBufferOptions _options;
    private readonly ILogger<RedisConversationBuffer> _logger;

    public RedisConversationBuffer(
        IConnectionMultiplexer redis,
        IOptions<ConversationBufferOptions> options,
        ILogger<RedisConversationBuffer> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    private static RedisKey GetKey(string userId, string sessionId) =>
        new($"conv-buffer:{userId}:{sessionId}");

    public async Task AppendTurnAsync(string userId, string sessionId, string role, string content, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetKey(userId, sessionId);

            var turn = new ConversationTurn(role, content, DateTime.UtcNow);
            var json = JsonSerializer.Serialize(turn);

            await db.ListRightPushAsync(key, json);

            // Cap the buffer at MaxTurns (keeps the most recent MaxTurns)
            if (_options.MaxTurns > 0)
            {
                await db.ListTrimAsync(key, -_options.MaxTurns, -1);
            }

            // Refresh sliding session TTL
            if (_options.SessionTtlMinutes > 0)
            {
                await db.KeyExpireAsync(key, TimeSpan.FromMinutes(_options.SessionTtlMinutes));
            }

            _logger.LogDebug("Appended turn ({Role}) to Redis buffer for user {UserId}, session {SessionId}",
                role, userId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append turn to Redis buffer for user {UserId}, session {SessionId}.",
                userId, sessionId);
        }
    }

    public async Task<IReadOnlyList<ConversationTurn>> GetRecentTurnsAsync(string userId, string sessionId, int maxTurns, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetKey(userId, sessionId);

            var takeCount = Math.Max(1, maxTurns);
            var values = await db.ListRangeAsync(key, -takeCount, -1);

            if (values == null || values.Length == 0)
            {
                return Array.Empty<ConversationTurn>();
            }

            var turns = new List<ConversationTurn>(values.Length);
            foreach (var item in values)
            {
                if (!item.IsNullOrEmpty)
                {
                    var turn = JsonSerializer.Deserialize<ConversationTurn>(item.ToString());
                    if (turn != null)
                    {
                        turns.Add(turn);
                    }
                }
            }

            return turns;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read recent turns from Redis buffer for user {UserId}, session {SessionId}.",
                userId, sessionId);
            return Array.Empty<ConversationTurn>();
        }
    }

    public async Task<int> GetTurnCountAsync(string userId, string sessionId, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetKey(userId, sessionId);
            var count = await db.ListLengthAsync(key);
            return (int)count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get turn count from Redis buffer for user {UserId}, session {SessionId}.",
                userId, sessionId);
            return 0;
        }
    }

    public async Task<IReadOnlyList<ConversationTurn>> FlushAndClearAsync(string userId, string sessionId, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetKey(userId, sessionId);

            var values = await db.ListRangeAsync(key, 0, -1);
            await db.KeyDeleteAsync(key);

            if (values == null || values.Length == 0)
            {
                return Array.Empty<ConversationTurn>();
            }

            var turns = new List<ConversationTurn>(values.Length);
            foreach (var item in values)
            {
                if (!item.IsNullOrEmpty)
                {
                    var turn = JsonSerializer.Deserialize<ConversationTurn>(item.ToString());
                    if (turn != null)
                    {
                        turns.Add(turn);
                    }
                }
            }

            _logger.LogInformation("Flushed and cleared {Count} turns from Redis buffer for user {UserId}, session {SessionId}",
                turns.Count, userId, sessionId);

            return turns;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush Redis buffer for user {UserId}, session {SessionId}.",
                userId, sessionId);
            return Array.Empty<ConversationTurn>();
        }
    }

    public async Task ClearAsync(string userId, string sessionId, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetKey(userId, sessionId);
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear Redis buffer for user {UserId}, session {SessionId}.",
                userId, sessionId);
        }
    }
}
