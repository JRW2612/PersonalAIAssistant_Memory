namespace PersonalAIAssistant.Memory.Core.Interfaces.Security;

public interface IProviderTokenService
{
    string EncryptToken(string plainToken);
    string DecryptToken(string encryptedToken);
    Task<string?> GetValidAccessTokenAsync(string userId, string provider, string requiredScope, CancellationToken ct = default);
    Task<bool> HasScopeConsentAsync(string userId, string provider, string requiredScope, CancellationToken ct = default);
}
