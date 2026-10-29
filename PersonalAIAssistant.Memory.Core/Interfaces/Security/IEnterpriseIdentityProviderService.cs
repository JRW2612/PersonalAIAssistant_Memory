using System.Security.Claims;
using PersonalAIAssistant.Memory.Core.Models.Security;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Security
{
    /// <summary>
    /// Service for Enterprise SSO federation with OIDC (Entra ID, PingOne) and SAML 2.0 (PingFederate, ADFS).
    /// </summary>
    public interface IEnterpriseIdentityProviderService
    {
        /// <summary>
        /// Validates an enterprise OIDC ID token, verifies signature and tenant boundaries, and transforms claims.
        /// </summary>
        Task<ClaimsPrincipal> ValidateOidcTokenAsync(string idToken, string tenantId, CancellationToken ct = default);

        /// <summary>
        /// Validates an enterprise SAML 2.0 Response/Assertion XML, checks X.509 signature and issuer, and extracts attributes.
        /// </summary>
        Task<ClaimsPrincipal> ValidateSamlAssertionAsync(string samlResponseXml, string tenantId, CancellationToken ct = default);

        /// <summary>
        /// Exchanges validated enterprise identity claims for an internal scoped JWT session bearer token.
        /// </summary>
        Task<string> GenerateServiceSessionJwtAsync(ClaimsPrincipal enterprisePrincipal, string tenantId, CancellationToken ct = default);

        /// <summary>
        /// Resolves enterprise IdP configuration for a tenant.
        /// </summary>
        Task<EnterpriseIdpConfig?> GetConfigByTenantAsync(string tenantId, CancellationToken ct = default);

        /// <summary>
        /// Resolves enterprise IdP configuration by corporate domain (e.g. "hdfcbank.com").
        /// </summary>
        Task<EnterpriseIdpConfig?> GetConfigByDomainAsync(string emailDomain, CancellationToken ct = default);

        /// <summary>
        /// Generates SAML 2.0 Service Provider (SP) Metadata XML for enterprise IT onboarding.
        /// </summary>
        string GenerateSpMetadataXml(string tenantId);
    }
}
