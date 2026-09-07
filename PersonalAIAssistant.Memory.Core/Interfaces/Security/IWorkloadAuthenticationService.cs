using PersonalAIAssistant.Memory.Core.Entities;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Security
{
    public interface IWorkloadAuthenticationService
    {
        Task<WorkloadIdentityEntity?> AuthenticateClientCredentialsAsync(string clientId, string clientSecret, CancellationToken ct = default);
        Task<WorkloadIdentityEntity?> AuthenticateOidcTokenAsync(string rawJwtToken, CancellationToken ct = default);
        Task<bool> VerifyWebhookSignatureAsync(string clientId, string rawBody, string signatureHeader, CancellationToken ct = default);
    }
}
