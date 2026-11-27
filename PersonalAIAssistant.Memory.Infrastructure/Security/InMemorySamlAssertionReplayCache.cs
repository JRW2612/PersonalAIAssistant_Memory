using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    /// <summary>
    /// Thread-safe in-memory replay protection cache for consumed SAML 2.0 assertion IDs.
    /// Registered as a singleton so replay protection spans across HTTP requests.
    /// </summary>
    public sealed class InMemorySamlAssertionReplayCache : ISamlAssertionReplayCache
    {
        private const int CleanupThreshold = 1024;
        private readonly object _sync = new();
        private readonly Dictionary<string, DateTimeOffset> _consumed = new(StringComparer.Ordinal);

        public bool TryConsume(string tenantId, string assertionId, DateTimeOffset expiresAtUtc)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(assertionId))
            {
                return false;
            }

            var utcNow = DateTimeOffset.UtcNow;
            var key = $"{tenantId.Trim().ToUpperInvariant()}:{assertionId.Trim()}";

            lock (_sync)
            {
                if (_consumed.Count >= CleanupThreshold)
                {
                    PurgeExpiredLocked(utcNow);
                }

                if (_consumed.TryGetValue(key, out var existingExpiry) && existingExpiry > utcNow)
                {
                    return false;
                }

                _consumed[key] = expiresAtUtc;
                return true;
            }
        }

        private void PurgeExpiredLocked(DateTimeOffset utcNow)
        {
            var expiredKeys = _consumed
                .Where(kvp => kvp.Value <= utcNow)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _consumed.Remove(key);
            }
        }
    }
}
