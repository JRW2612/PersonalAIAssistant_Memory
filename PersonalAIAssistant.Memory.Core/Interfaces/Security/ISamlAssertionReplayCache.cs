namespace PersonalAIAssistant.Memory.Core.Interfaces.Security
{
    /// <summary>
    /// Replay protection cache for validated SAML 2.0 assertion IDs.
    /// </summary>
    public interface ISamlAssertionReplayCache
    {
        /// <summary>
        /// Attempts to record an assertion ID as consumed until <paramref name="expiresAtUtc"/>.
        /// Returns <c>true</c> if the assertion ID has not been seen before; <c>false</c> on replay.
        /// </summary>
        bool TryConsume(string tenantId, string assertionId, DateTimeOffset expiresAtUtc);
    }
}
