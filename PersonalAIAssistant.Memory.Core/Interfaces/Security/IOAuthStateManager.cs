namespace PersonalAIAssistant.Memory.Core.Interfaces.Security;

public sealed record OAuthState(string UserId, string TenantId, string Provider, IReadOnlyList<string> Scopes, DateTime ExpiresAtUtc);

public interface IOAuthStateManager
{
    string Create(string userId, string tenantId, string provider, IEnumerable<string> scopes);
    OAuthState Consume(string state);
}
