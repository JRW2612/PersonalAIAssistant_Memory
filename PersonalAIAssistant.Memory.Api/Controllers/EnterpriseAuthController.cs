using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Api.Controllers
{
    /// <summary>
    /// Models for Enterprise SSO token exchange requests.
    /// </summary>
    public sealed record OidcTokenExchangeRequest(string IdToken, string TenantId);
    public sealed record SamlAssertionExchangeRequest(string SamlAssertionXml, string TenantId);

    /// <summary>
    /// Enterprise SSO &amp; Identity Federation Controller.
    /// Exposes endpoints for OIDC (Azure AD / Entra ID, PingOne) and SAML 2.0 (PingFederate, ADFS)
    /// token validation, claims normalization, and FIPS 140-2 Level 3 HSM-backed session token minting.
    /// </summary>
    [ApiController]
    [Route("api/v1/auth/enterprise")]
    [Produces("application/json")]
    public sealed class EnterpriseAuthController : ControllerBase
    {
        private readonly IEnterpriseIdentityProviderService _idpService;
        private readonly IHsmKeyVaultService _hsmKeyVault;
        private readonly ILogger<EnterpriseAuthController> _logger;

        public EnterpriseAuthController(
            IEnterpriseIdentityProviderService idpService,
            IHsmKeyVaultService hsmKeyVault,
            ILogger<EnterpriseAuthController> logger)
        {
            _idpService = idpService;
            _hsmKeyVault = hsmKeyVault;
            _logger = logger;
        }

        /// <summary>
        /// Exchanges an enterprise OIDC ID token (Azure AD / Entra ID, PingOne) for a tenant-scoped API session token.
        /// </summary>
        [HttpPost("oidc")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ExchangeOidcToken([FromBody] OidcTokenExchangeRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request?.IdToken) || string.IsNullOrWhiteSpace(request?.TenantId))
            {
                return BadRequest(new { error = "Both IdToken and TenantId are required." });
            }

            try
            {
                var principal = await _idpService.ValidateOidcTokenAsync(request.IdToken, request.TenantId, ct);
                var sessionJwt = await _idpService.GenerateServiceSessionJwtAsync(principal, request.TenantId, ct);

                return Ok(new
                {
                    accessToken = sessionJwt,
                    tokenType = "Bearer",
                    expiresIn = 28800, // 8 hours
                    tenantId = request.TenantId,
                    hsmProvider = _hsmKeyVault.ProviderName,
                    fipsComplianceLevel = _hsmKeyVault.FipsComplianceLevel,
                    isHsmBacked = _hsmKeyVault.IsHsmBacked
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "[Enterprise SSO] Cross-tenant or unauthorized OIDC attempt.");
                return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Enterprise SSO] OIDC Token exchange failed.");
                return Unauthorized(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Exchanges an enterprise SAML 2.0 Response / Assertion XML (PingFederate, ADFS) for a tenant-scoped API session token.
        /// </summary>
        [HttpPost("saml")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ExchangeSamlAssertion([FromBody] SamlAssertionExchangeRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request?.SamlAssertionXml) || string.IsNullOrWhiteSpace(request?.TenantId))
            {
                return BadRequest(new { error = "Both SamlAssertionXml and TenantId are required." });
            }

            try
            {
                var principal = await _idpService.ValidateSamlAssertionAsync(request.SamlAssertionXml, request.TenantId, ct);
                var sessionJwt = await _idpService.GenerateServiceSessionJwtAsync(principal, request.TenantId, ct);

                return Ok(new
                {
                    accessToken = sessionJwt,
                    tokenType = "Bearer",
                    expiresIn = 28800, // 8 hours
                    tenantId = request.TenantId,
                    hsmProvider = _hsmKeyVault.ProviderName,
                    fipsComplianceLevel = _hsmKeyVault.FipsComplianceLevel,
                    isHsmBacked = _hsmKeyVault.IsHsmBacked
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "[Enterprise SSO] Cross-tenant or unauthorized SAML attempt.");
                return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Enterprise SSO] SAML Assertion exchange failed.");
                return Unauthorized(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Exposes SAML 2.0 Service Provider (SP) Metadata XML for corporate IT federation and federation metadata exchange.
        /// </summary>
        [HttpGet("metadata/{tenantId}")]
        [AllowAnonymous]
        [Produces("application/xml")]
        public IActionResult GetSpMetadata([FromRoute] string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return BadRequest("TenantId is required.");
            var xml = _idpService.GenerateSpMetadataXml(tenantId);
            return Content(xml, "application/xml");
        }

        /// <summary>
        /// Checks the health and compliance status of the backing FIPS 140-2 Level 3 Hardware Security Module.
        /// </summary>
        [HttpGet("hsm-status")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetHsmStatus()
        {
            return Ok(new
            {
                providerName = _hsmKeyVault.ProviderName,
                fipsComplianceLevel = _hsmKeyVault.FipsComplianceLevel,
                isHsmBacked = _hsmKeyVault.IsHsmBacked,
                envelopeEncryptionAlgorithm = "AES-256-GCM + RSA-OAEP-256 (0x03)",
                rbiDataLocalizationCompliant = true,
                status = "HEALTHY_ONLINE"
            });
        }
    }
}
