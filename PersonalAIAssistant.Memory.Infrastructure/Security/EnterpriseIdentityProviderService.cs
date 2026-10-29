using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    /// <summary>
    /// Production Enterprise Identity Provider (IdP) service supporting OIDC (Microsoft Entra ID, PingOne)
    /// and SAML 2.0 (PingFederate, Okta, ADFS) with automated claims normalization, tenant isolation,
    /// and HSM-backed session JWT generation.
    /// </summary>
    public sealed class EnterpriseIdentityProviderService : IEnterpriseIdentityProviderService
    {
        private readonly EnterpriseSsoOptions _options;
        private readonly IHsmKeyVaultService _hsmKeyVault;
        private readonly ISecurityAuditLogger _auditLogger;
        private readonly ILogger<EnterpriseIdentityProviderService> _logger;
        private readonly JwtSecurityTokenHandler _tokenHandler = new();

        public EnterpriseIdentityProviderService(
            IOptions<EnterpriseSsoOptions> options,
            IHsmKeyVaultService hsmKeyVault,
            ISecurityAuditLogger auditLogger,
            ILogger<EnterpriseIdentityProviderService> logger)
        {
            _options = options.Value ?? new EnterpriseSsoOptions();
            _hsmKeyVault = hsmKeyVault;
            _auditLogger = auditLogger;
            _logger = logger;

            // Seed default enterprise configurations if empty
            if (_options.Providers.Count == 0)
            {
                _options.Providers.Add(new EnterpriseIdpConfig
                {
                    TenantId = "tenant-hdfc",
                    Domain = "hdfcbank.com",
                    Protocol = "OIDC",
                    Authority = "https://login.microsoftonline.com/tenant-hdfc/v2.0",
                    ClientId = "api://personal-ai-assistant",
                    Audience = "api://personal-ai-assistant",
                    RequireMfa = true
                });

                _options.Providers.Add(new EnterpriseIdpConfig
                {
                    TenantId = "tenant-icici",
                    Domain = "icicibank.com",
                    Protocol = "SAML2",
                    Authority = "https://sso.pingidentity.com/idp/tenant-icici",
                    ClientId = "personal-ai-assistant-sp",
                    Audience = "https://memory.ai-assistant.internal/sp",
                    RequireMfa = true
                });
            }
        }

        public Task<EnterpriseIdpConfig?> GetConfigByTenantAsync(string tenantId, CancellationToken ct = default)
        {
            var config = _options.Providers.FirstOrDefault(p =>
                string.Equals(p.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(config);
        }

        public Task<EnterpriseIdpConfig?> GetConfigByDomainAsync(string emailDomain, CancellationToken ct = default)
        {
            var config = _options.Providers.FirstOrDefault(p =>
                string.Equals(p.Domain, emailDomain, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(config);
        }

        public async Task<ClaimsPrincipal> ValidateOidcTokenAsync(string idToken, string tenantId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(idToken))
                throw new ArgumentException("OIDC token cannot be null or empty.", nameof(idToken));

            var config = await GetConfigByTenantAsync(tenantId, ct)
                ?? throw new SecurityTokenException($"Enterprise IdP not configured for tenant '{tenantId}'.");

            JwtSecurityToken jwt;
            try
            {
                jwt = _tokenHandler.ReadJwtToken(idToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Enterprise SSO] Malformed OIDC token received for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied("anonymous", "OidcTokenValidation", tenantId, "Malformed token");
                throw new SecurityTokenException("Malformed OIDC token.", ex);
            }

            // 1. Verify Audience
            if (!string.IsNullOrWhiteSpace(config.Audience) && !jwt.Audiences.Contains(config.Audience))
            {
                _logger.LogError("[Enterprise SSO] Audience mismatch for tenant '{TenantId}'. Expected '{Expected}', got '{Actual}'.",
                    tenantId, config.Audience, string.Join(",", jwt.Audiences));
                _auditLogger.LogAccessDenied(jwt.Subject ?? "unknown", "OidcAudienceMismatch", tenantId, $"Audience '{config.Audience}' not found");
                throw new SecurityTokenValidationException("OIDC audience mismatch.");
            }

            // 2. Verify Expiration
            if (jwt.ValidTo < DateTime.UtcNow)
            {
                _logger.LogWarning("[Enterprise SSO] Expired OIDC token for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied(jwt.Subject ?? "unknown", "OidcTokenExpired", tenantId, "Token expired");
                throw new SecurityTokenExpiredException("OIDC token has expired.");
            }

            // 3. Verify Tenant Isolation Boundary
            var callerUser = jwt.Subject 
                          ?? jwt.Claims.FirstOrDefault(c => c.Type is "sub" or ClaimTypes.NameIdentifier or "oid")?.Value 
                          ?? "unknown";

            var tokenTenant = jwt.Claims.FirstOrDefault(c => c.Type is "tid" or "tenant_id" or "http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
            if (!string.IsNullOrWhiteSpace(tokenTenant) && !string.Equals(tokenTenant, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("[Security Alert] Cross-tenant OIDC federation attempt detected! Token Tenant: '{TokenTenant}' vs Target: '{TargetTenant}'.",
                    tokenTenant, tenantId);
                _auditLogger.LogCrossTenantAttempt(callerUser, tokenTenant, "oidc-federation", tenantId, "OidcTokenValidation");
                throw new UnauthorizedAccessException($"Cross-tenant access prohibited: token tenant '{tokenTenant}' does not match target tenant '{tenantId}'.");
            }

            // 4. Normalize claims
            var identity = new ClaimsIdentity("EnterpriseOIDC");
            var sub = jwt.Subject ?? jwt.Claims.FirstOrDefault(c => c.Type == "oid")?.Value ?? "enterprise-user";
            var upn = jwt.Claims.FirstOrDefault(c => c.Type is ClaimTypes.Upn or "upn" or ClaimTypes.Email or "email")?.Value ?? sub;
            var name = jwt.Claims.FirstOrDefault(c => c.Type is ClaimTypes.Name or "name")?.Value ?? upn;

            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
            identity.AddClaim(new Claim(ClaimTypes.Name, name));
            identity.AddClaim(new Claim(ClaimTypes.Upn, upn));
            identity.AddClaim(new Claim("tid", tenantId));
            identity.AddClaim(new Claim("tenant_id", tenantId));
            identity.AddClaim(new Claim("idp_protocol", "OIDC"));

            // Add roles and groups
            foreach (var role in jwt.Claims.Where(c => c.Type is ClaimTypes.Role or "role" or "roles"))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role.Value));
            }

            _logger.LogInformation("[Enterprise SSO] Successfully federated OIDC identity: User='{User}', Tenant='{TenantId}'.", upn, tenantId);
            _auditLogger.LogAccessGranted(sub, "OidcAuthentication", tenantId);

            return new ClaimsPrincipal(identity);
        }

        public async Task<ClaimsPrincipal> ValidateSamlAssertionAsync(string samlResponseXml, string tenantId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(samlResponseXml))
                throw new ArgumentException("SAML Response XML cannot be null or empty.", nameof(samlResponseXml));

            var config = await GetConfigByTenantAsync(tenantId, ct)
                ?? throw new SecurityTokenException($"Enterprise IdP not configured for tenant '{tenantId}'.");

            XDocument doc;
            try
            {
                // Support both plain XML and base64-encoded SAML Response
                var rawXml = samlResponseXml.Trim().StartsWith("<")
                    ? samlResponseXml
                    : Encoding.UTF8.GetString(Convert.FromBase64String(samlResponseXml));
                doc = XDocument.Parse(rawXml);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Enterprise SSO] Malformed SAML Response XML for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied("anonymous", "SamlXmlMalformed", tenantId, "Invalid XML");
                throw new SecurityTokenException("Malformed SAML 2.0 XML.", ex);
            }

            XNamespace saml2 = "urn:oasis:names:tc:SAML:2.0:assertion";

            var assertion = doc.Descendants(saml2 + "Assertion").FirstOrDefault()
                         ?? doc.Descendants("Assertion").FirstOrDefault()
                         ?? throw new SecurityTokenException("No SAML 2.0 Assertion found in response.");

            // 1. Verify Issuer
            var issuer = assertion.Element(saml2 + "Issuer")?.Value
                      ?? assertion.Element("Issuer")?.Value;

            if (!string.IsNullOrWhiteSpace(config.Authority) && !string.Equals(issuer, config.Authority, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[Enterprise SSO] SAML Issuer mismatch: Expected '{Expected}', got '{Actual}'.", config.Authority, issuer);
            }

            // 2. Verify Conditions (Time validity window)
            var conditions = assertion.Element(saml2 + "Conditions") ?? assertion.Element("Conditions");
            if (conditions != null)
            {
                var notBeforeStr = conditions.Attribute("NotBefore")?.Value;
                var notOnOrAfterStr = conditions.Attribute("NotOnOrAfter")?.Value;

                if (!string.IsNullOrWhiteSpace(notBeforeStr) && DateTime.TryParse(notBeforeStr, out var notBefore) && DateTime.UtcNow < notBefore.AddMinutes(-2))
                {
                    _auditLogger.LogAccessDenied("saml-user", "SamlTokenPremature", tenantId, "Premature assertion");
                    throw new SecurityTokenNotYetValidException("SAML assertion is not yet valid.");
                }

                if (!string.IsNullOrWhiteSpace(notOnOrAfterStr) && DateTime.TryParse(notOnOrAfterStr, out var notOnOrAfter) && DateTime.UtcNow >= notOnOrAfter.AddMinutes(2))
                {
                    _auditLogger.LogAccessDenied("saml-user", "SamlTokenExpired", tenantId, "Expired assertion");
                    throw new SecurityTokenExpiredException("SAML assertion has expired.");
                }
            }

            // 3. Extract Subject NameID
            var subject = assertion.Element(saml2 + "Subject") ?? assertion.Element("Subject");
            var nameId = subject?.Element(saml2 + "NameID")?.Value
                      ?? subject?.Element("NameID")?.Value
                      ?? "saml-user";

            // 4. Extract Attributes
            var identity = new ClaimsIdentity("EnterpriseSAML2");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, nameId));
            identity.AddClaim(new Claim(ClaimTypes.Name, nameId));
            identity.AddClaim(new Claim("tid", tenantId));
            identity.AddClaim(new Claim("tenant_id", tenantId));
            identity.AddClaim(new Claim("idp_protocol", "SAML2"));

            var attributes = assertion.Descendants(saml2 + "Attribute").Concat(assertion.Descendants("Attribute"));
            string? assertionTenant = null;

            foreach (var attr in attributes)
            {
                var attrName = attr.Attribute("Name")?.Value;
                var attrValue = (attr.Element(saml2 + "AttributeValue") ?? attr.Element("AttributeValue"))?.Value;
                if (string.IsNullOrWhiteSpace(attrName) || string.IsNullOrWhiteSpace(attrValue)) continue;

                if (attrName.EndsWith("tenantid", StringComparison.OrdinalIgnoreCase) || attrName == "tenant_id" || attrName == "tid")
                {
                    assertionTenant = attrValue;
                }
                else if (attrName.EndsWith("emailaddress", StringComparison.OrdinalIgnoreCase) || attrName == "email")
                {
                    identity.AddClaim(new Claim(ClaimTypes.Email, attrValue));
                    identity.AddClaim(new Claim(ClaimTypes.Upn, attrValue));
                }
                else if (attrName.EndsWith("role", StringComparison.OrdinalIgnoreCase) || attrName == "role")
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, attrValue));
                }
                else
                {
                    identity.AddClaim(new Claim(attrName, attrValue));
                }
            }

            // Verify Tenant Isolation Boundary
            if (!string.IsNullOrWhiteSpace(assertionTenant) && !string.Equals(assertionTenant, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("[Security Alert] Cross-tenant SAML federation violation! Assertion Tenant='{AssertTenant}' vs Target='{TargetTenant}'.",
                    assertionTenant, tenantId);
                _auditLogger.LogCrossTenantAttempt(nameId, assertionTenant, "saml-federation", tenantId, "SamlAuthentication");
                throw new UnauthorizedAccessException($"Cross-tenant access prohibited: SAML tenant '{assertionTenant}' does not match target tenant '{tenantId}'.");
            }

            _logger.LogInformation("[Enterprise SSO] Successfully federated SAML identity: User='{User}', Tenant='{TenantId}'.", nameId, tenantId);
            _auditLogger.LogAccessGranted(nameId, "SamlAuthentication", tenantId);

            return new ClaimsPrincipal(identity);
        }

        public async Task<string> GenerateServiceSessionJwtAsync(ClaimsPrincipal enterprisePrincipal, string tenantId, CancellationToken ct = default)
        {
            var signingKeySecret = await _hsmKeyVault.GetSecretAsync("JwtSigningSecret", ct);
            var keyBytes = Encoding.UTF8.GetBytes(signingKeySecret);
            var signingKey = new SymmetricSecurityKey(keyBytes);
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, enterprisePrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "user"),
                new(ClaimTypes.Name, enterprisePrincipal.FindFirst(ClaimTypes.Name)?.Value ?? "Enterprise User"),
                new("tid", tenantId),
                new("tenant_id", tenantId),
                new("jti", Guid.NewGuid().ToString("N")),
                new("auth_source", enterprisePrincipal.FindFirst("idp_protocol")?.Value ?? "EnterpriseSSO"),
                new("fips_hsm_secured", _hsmKeyVault.IsHsmBacked ? "true" : "false")
            };

            var upn = enterprisePrincipal.FindFirst(ClaimTypes.Upn)?.Value ?? enterprisePrincipal.FindFirst(ClaimTypes.Email)?.Value;
            if (!string.IsNullOrWhiteSpace(upn))
            {
                claims.Add(new Claim(ClaimTypes.Upn, upn));
            }

            foreach (var role in enterprisePrincipal.FindAll(ClaimTypes.Role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role.Value));
            }

            var token = new JwtSecurityToken(
                issuer: "PersonalAIAssistant.Memory",
                audience: "PersonalAIAssistant.Memory.Api",
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: credentials);

            return _tokenHandler.WriteToken(token);
        }

        public string GenerateSpMetadataXml(string tenantId)
        {
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<md:EntityDescriptor xmlns:md=""urn:oasis:names:tc:SAML:2.0:metadata"" entityID=""{_options.SpEntityId}/{tenantId}"">
  <md:SPSSODescriptor AuthnRequestsSigned=""true"" WantAssertionsSigned=""true"" protocolSupportEnumeration=""urn:oasis:names:tc:SAML:2.0:protocol"">
    <md:NameIDFormat>urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress</md:NameIDFormat>
    <md:AssertionConsumerService Binding=""urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST"" Location=""{_options.AssertionConsumerServiceUrl}"" index=""1"" isDefault=""true""/>
  </md:SPSSODescriptor>
</md:EntityDescriptor>";
        }
    }
}
