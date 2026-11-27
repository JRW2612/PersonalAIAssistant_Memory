using System.Collections.Concurrent;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    /// <summary>
    /// Production Enterprise Identity Provider (IdP) service supporting OIDC (Microsoft Entra ID, PingOne)
    /// and SAML 2.0 (PingFederate, Okta, ADFS) with cryptographic signature verification, replay protection,
    /// claims normalization, tenant isolation, and session JWT generation aligned with API bearer validation.
    /// </summary>
    public sealed class EnterpriseIdentityProviderService : IEnterpriseIdentityProviderService
    {
        private const int MaxOidcTokenLength = 32_768;
        private const int MaxSamlPayloadLength = 524_288;
        private const string Saml2ProtocolNamespace = "urn:oasis:names:tc:SAML:2.0:protocol";
        private const string Saml2AssertionNamespace = "urn:oasis:names:tc:SAML:2.0:assertion";
        private const string Saml2BearerMethod = "urn:oasis:names:tc:SAML:2.0:cm:bearer";
        private const string XmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";
        private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

        private static readonly string[] AllowedOidcAlgorithms =
        [
            SecurityAlgorithms.RsaSha256,
            SecurityAlgorithms.RsaSha384,
            SecurityAlgorithms.RsaSha512,
            SecurityAlgorithms.RsaSsaPssSha256,
            SecurityAlgorithms.RsaSsaPssSha384,
            SecurityAlgorithms.RsaSsaPssSha512,
            SecurityAlgorithms.EcdsaSha256,
            SecurityAlgorithms.EcdsaSha384,
            SecurityAlgorithms.EcdsaSha512
        ];

        private static readonly HashSet<string> AllowedXmlTransforms = new(StringComparer.Ordinal)
        {
            SignedXml.XmlDsigEnvelopedSignatureTransformUrl,
            SignedXml.XmlDsigExcC14NTransformUrl,
            SignedXml.XmlDsigExcC14NWithCommentsTransformUrl,
            SignedXml.XmlDsigC14NTransformUrl,
            SignedXml.XmlDsigC14NWithCommentsTransformUrl
        };

        private static readonly HashSet<string> ReservedClaimTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "tid",
            "tenant_id",
            "idp_protocol",
            "auth_source",
            "fips_hsm_secured",
            "jti"
        };

        private readonly EnterpriseSsoOptions _options;
        private readonly JwtOptions _jwtOptions;
        private readonly HsmOptions _hsmOptions;
        private readonly IHsmKeyVaultService _hsmKeyVault;
        private readonly ISecurityAuditLogger _auditLogger;
        private readonly ILogger<EnterpriseIdentityProviderService> _logger;
        private readonly ISamlAssertionReplayCache _replayCache;
        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly JwtSecurityTokenHandler _tokenHandler = new();
        private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _oidcConfigManagers =
            new(StringComparer.OrdinalIgnoreCase);

        public EnterpriseIdentityProviderService(
            IOptions<EnterpriseSsoOptions> options,
            IHsmKeyVaultService hsmKeyVault,
            ISecurityAuditLogger auditLogger,
            ILogger<EnterpriseIdentityProviderService> logger,
            IOptions<JwtOptions>? jwtOptions = null,
            IOptions<HsmOptions>? hsmOptions = null,
            ISamlAssertionReplayCache? replayCache = null,
            IHttpClientFactory? httpClientFactory = null)
        {
            _options = options.Value ?? new EnterpriseSsoOptions();
            _jwtOptions = jwtOptions?.Value ?? new JwtOptions();
            _hsmOptions = hsmOptions?.Value ?? new HsmOptions();
            _hsmKeyVault = hsmKeyVault;
            _auditLogger = auditLogger;
            _logger = logger;
            _replayCache = replayCache ?? new InMemorySamlAssertionReplayCache();
            _httpClientFactory = httpClientFactory;

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
            if (idToken.Length > MaxOidcTokenLength)
                throw new ArgumentException("OIDC token exceeds maximum allowed size.", nameof(idToken));
            if (string.IsNullOrWhiteSpace(tenantId))
                throw new ArgumentException("TenantId cannot be null or empty.", nameof(tenantId));

            var config = await GetConfigByTenantAsync(tenantId, ct)
                ?? throw new SecurityTokenException($"Enterprise IdP not configured for tenant '{tenantId}'.");

            var expectedIssuer = !string.IsNullOrWhiteSpace(config.ValidIssuer) ? config.ValidIssuer : config.Authority;
            if (string.IsNullOrWhiteSpace(expectedIssuer))
            {
                _auditLogger.LogAccessDenied("anonymous", "OidcConfigInvalid", tenantId, "OIDC issuer/authority not configured");
                throw new SecurityTokenException($"OIDC issuer is not configured for tenant '{tenantId}'.");
            }

            var expectedAudience = !string.IsNullOrWhiteSpace(config.Audience) ? config.Audience : config.ClientId;
            if (string.IsNullOrWhiteSpace(expectedAudience))
            {
                _auditLogger.LogAccessDenied("anonymous", "OidcConfigInvalid", tenantId, "OIDC audience not configured");
                throw new SecurityTokenException($"OIDC audience is not configured for tenant '{tenantId}'.");
            }

            var signingKeys = await ResolveOidcSigningKeysAsync(config, tenantId, ct);
            if (signingKeys.Count == 0)
            {
                _auditLogger.LogAccessDenied("anonymous", "OidcNoSigningKeys", tenantId, "No trusted signing keys resolved");
                throw new SecurityTokenInvalidSignatureException("No trusted OIDC signing keys are configured for tenant.");
            }

            var validationParameters = new TokenValidationParameters
            {
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeys,
                ValidAlgorithms = AllowedOidcAlgorithms,
                ValidateIssuer = true,
                ValidIssuer = expectedIssuer,
                ValidateAudience = true,
                ValidAudience = expectedAudience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = ClockSkew
            };

            ClaimsPrincipal validatedPrincipal;
            try
            {
                validatedPrincipal = _tokenHandler.ValidateToken(idToken, validationParameters, out _);
            }
            catch (SecurityTokenExpiredException ex)
            {
                _logger.LogWarning(ex, "[Enterprise SSO] Expired OIDC token for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied("unknown", "OidcTokenExpired", tenantId, "Token expired");
                throw;
            }
            catch (SecurityTokenInvalidAudienceException ex)
            {
                _logger.LogError(ex, "[Enterprise SSO] Audience mismatch for tenant '{TenantId}'. Expected '{Expected}'.",
                    tenantId, expectedAudience);
                _auditLogger.LogAccessDenied("unknown", "OidcAudienceMismatch", tenantId, $"Audience '{expectedAudience}' not matched");
                throw;
            }
            catch (SecurityTokenInvalidIssuerException ex)
            {
                _logger.LogError(ex, "[Enterprise SSO] Issuer mismatch for tenant '{TenantId}'. Expected '{Expected}'.",
                    tenantId, expectedIssuer);
                _auditLogger.LogAccessDenied("unknown", "OidcIssuerMismatch", tenantId, $"Issuer '{expectedIssuer}' not matched");
                throw;
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogWarning(ex, "[Enterprise SSO] OIDC token signature or validation failed for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied("anonymous", "OidcTokenValidation", tenantId, "Token signature or validation failed");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Enterprise SSO] Malformed OIDC token received for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied("anonymous", "OidcTokenValidation", tenantId, "Malformed token");
                throw new SecurityTokenException("Malformed OIDC token.", ex);
            }

            // Require authenticated subject claim
            var sub = validatedPrincipal.FindFirst("sub")?.Value
                   ?? validatedPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? validatedPrincipal.FindFirst("oid")?.Value;

            if (string.IsNullOrWhiteSpace(sub))
            {
                _logger.LogWarning("[Enterprise SSO] OIDC token missing required subject claim for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied("unknown", "OidcMissingSubject", tenantId, "Missing subject claim");
                throw new SecurityTokenValidationException("OIDC token is missing required subject claim.");
            }

            // Enforce mandatory tenant claim and tenant isolation boundary
            var tokenTenant = validatedPrincipal.FindFirst("tid")?.Value
                           ?? validatedPrincipal.FindFirst("tenant_id")?.Value
                           ?? validatedPrincipal.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

            if (string.IsNullOrWhiteSpace(tokenTenant))
            {
                _logger.LogWarning("[Enterprise SSO] OIDC token missing required tenant claim for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied(sub, "OidcMissingTenantClaim", tenantId, "Missing required tenant claim");
                throw new SecurityTokenValidationException("OIDC token is missing required tenant claim.");
            }

            if (!string.Equals(tokenTenant, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("[Security Alert] Cross-tenant OIDC federation attempt detected! Token Tenant: '{TokenTenant}' vs Target: '{TargetTenant}'.",
                    tokenTenant, tenantId);
                _auditLogger.LogCrossTenantAttempt(sub, tokenTenant, "oidc-federation", tenantId, "OidcTokenValidation");
                throw new UnauthorizedAccessException($"Cross-tenant access prohibited: token tenant '{tokenTenant}' does not match target tenant '{tenantId}'.");
            }

            // Normalize claims
            var identity = new ClaimsIdentity("EnterpriseOIDC");
            var upn = validatedPrincipal.FindFirst(ClaimTypes.Upn)?.Value
                   ?? validatedPrincipal.FindFirst("upn")?.Value
                   ?? validatedPrincipal.FindFirst(ClaimTypes.Email)?.Value
                   ?? validatedPrincipal.FindFirst("email")?.Value
                   ?? sub;
            var name = validatedPrincipal.FindFirst(ClaimTypes.Name)?.Value
                    ?? validatedPrincipal.FindFirst("name")?.Value
                    ?? upn;

            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
            identity.AddClaim(new Claim(ClaimTypes.Name, name));
            identity.AddClaim(new Claim(ClaimTypes.Upn, upn));
            identity.AddClaim(new Claim("tid", tenantId));
            identity.AddClaim(new Claim("tenant_id", tenantId));
            identity.AddClaim(new Claim("idp_protocol", "OIDC"));

            foreach (var role in validatedPrincipal.Claims.Where(c => c.Type is ClaimTypes.Role or "role" or "roles"))
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
            if (samlResponseXml.Length > MaxSamlPayloadLength)
                throw new ArgumentException("SAML payload exceeds maximum allowed size.", nameof(samlResponseXml));
            if (string.IsNullOrWhiteSpace(tenantId))
                throw new ArgumentException("TenantId cannot be null or empty.", nameof(tenantId));

            var config = await GetConfigByTenantAsync(tenantId, ct)
                ?? throw new SecurityTokenException($"Enterprise IdP not configured for tenant '{tenantId}'.");

            var expectedIssuer = !string.IsNullOrWhiteSpace(config.ValidIssuer) ? config.ValidIssuer : config.Authority;
            if (string.IsNullOrWhiteSpace(expectedIssuer))
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlConfigInvalid", tenantId, "SAML issuer/authority not configured");
                throw new SecurityTokenException($"SAML issuer is not configured for tenant '{tenantId}'.");
            }

            var expectedAudience = !string.IsNullOrWhiteSpace(config.Audience) ? config.Audience : _options.SpEntityId;
            var expectedRecipient = !string.IsNullOrWhiteSpace(config.AssertionConsumerServiceUrl)
                ? config.AssertionConsumerServiceUrl
                : _options.AssertionConsumerServiceUrl;

            if (string.IsNullOrWhiteSpace(expectedAudience) || string.IsNullOrWhiteSpace(expectedRecipient))
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlConfigInvalid", tenantId, "SAML audience or ACS recipient not configured");
                throw new SecurityTokenException($"SAML audience or recipient is not configured for tenant '{tenantId}'.");
            }

            XmlDocument doc;
            try
            {
                var trimmed = samlResponseXml.Trim();
                var rawXml = trimmed.StartsWith('<')
                    ? samlResponseXml
                    : Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));

                if (rawXml.Length > MaxSamlPayloadLength)
                {
                    throw new SecurityTokenException("Decoded SAML XML exceeds maximum allowed size.");
                }

                doc = ParseXmlSafely(rawXml);
            }
            catch (SecurityTokenException)
            {
                _auditLogger.LogAccessDenied("anonymous", "SamlXmlMalformed", tenantId, "Oversized XML");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Enterprise SSO] Malformed SAML Response XML for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied("anonymous", "SamlXmlMalformed", tenantId, "Invalid XML");
                throw new SecurityTokenException("Malformed SAML 2.0 XML.", ex);
            }

            // 1. Structural & XML Signature Wrapping (XSW) validation
            var (responseElement, assertionElement) = ExtractValidatedSamlElements(doc, tenantId);

            // 2. Cryptographic XML Signature verification
            VerifySamlXmlSignature(doc, responseElement, assertionElement, config, tenantId);

            // 3. Verify Issuer (strictly enforced)
            var assertionIssuer = GetDirectChildElement(assertionElement, "Issuer", Saml2AssertionNamespace)?.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(assertionIssuer) ||
                !string.Equals(assertionIssuer, expectedIssuer, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("[Enterprise SSO] SAML Issuer mismatch for tenant '{TenantId}': Expected '{Expected}', got '{Actual}'.",
                    tenantId, expectedIssuer, assertionIssuer);
                _auditLogger.LogAccessDenied("saml-user", "SamlIssuerMismatch", tenantId, "Issuer mismatch");
                throw new SecurityTokenInvalidIssuerException("SAML assertion issuer mismatch.");
            }

            if (responseElement != null)
            {
                var responseIssuer = GetDirectChildElement(responseElement, "Issuer", Saml2AssertionNamespace)?.InnerText?.Trim();
                if (!string.IsNullOrWhiteSpace(responseIssuer) &&
                    !string.Equals(responseIssuer, expectedIssuer, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("[Enterprise SSO] SAML Response Issuer mismatch for tenant '{TenantId}': Expected '{Expected}', got '{Actual}'.",
                        tenantId, expectedIssuer, responseIssuer);
                    _auditLogger.LogAccessDenied("saml-user", "SamlIssuerMismatch", tenantId, "Response issuer mismatch");
                    throw new SecurityTokenInvalidIssuerException("SAML response issuer mismatch.");
                }
            }

            // 4. Verify Required Conditions (Time validity window & AudienceRestriction)
            var utcNow = DateTimeOffset.UtcNow;
            var conditions = GetDirectChildElement(assertionElement, "Conditions", Saml2AssertionNamespace);
            if (conditions == null)
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlMissingConditions", tenantId, "Missing Conditions element");
                throw new SecurityTokenValidationException("SAML assertion is missing required Conditions element.");
            }

            var notBeforeStr = conditions.GetAttribute("NotBefore");
            var notOnOrAfterStr = conditions.GetAttribute("NotOnOrAfter");
            if (!TryParseUtcTimestamp(notBeforeStr, out var notBefore) ||
                !TryParseUtcTimestamp(notOnOrAfterStr, out var notOnOrAfter))
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlInvalidConditions", tenantId, "Missing or invalid NotBefore/NotOnOrAfter");
                throw new SecurityTokenValidationException("SAML assertion Conditions must specify valid NotBefore and NotOnOrAfter timestamps.");
            }

            if (utcNow < notBefore.Subtract(ClockSkew))
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlTokenPremature", tenantId, "Premature assertion");
                throw new SecurityTokenNotYetValidException("SAML assertion is not yet valid.");
            }

            if (utcNow >= notOnOrAfter.Add(ClockSkew))
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlTokenExpired", tenantId, "Expired assertion");
                throw new SecurityTokenExpiredException("SAML assertion has expired.");
            }

            var audiences = GetDirectChildElements(conditions, "AudienceRestriction", Saml2AssertionNamespace)
                .SelectMany(ar => GetDirectChildElements(ar, "Audience", Saml2AssertionNamespace))
                .Select(a => a.InnerText.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .ToList();

            if (!audiences.Any(a => string.Equals(a, expectedAudience, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogError("[Enterprise SSO] SAML Audience mismatch for tenant '{TenantId}'. Expected '{Expected}'.",
                    tenantId, expectedAudience);
                _auditLogger.LogAccessDenied("saml-user", "SamlAudienceMismatch", tenantId, "Audience mismatch");
                throw new SecurityTokenInvalidAudienceException("SAML assertion audience mismatch.");
            }

            // 5. Verify Subject, NameID, and Bearer Recipient
            var subject = GetDirectChildElement(assertionElement, "Subject", Saml2AssertionNamespace);
            var nameId = subject != null
                ? GetDirectChildElement(subject, "NameID", Saml2AssertionNamespace)?.InnerText?.Trim()
                : null;

            if (subject == null || string.IsNullOrWhiteSpace(nameId))
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlMissingSubject", tenantId, "Missing Subject or NameID");
                throw new SecurityTokenValidationException("SAML assertion is missing required Subject NameID.");
            }

            var confirmationData = GetDirectChildElements(subject, "SubjectConfirmation", Saml2AssertionNamespace)
                .Where(sc =>
                {
                    var method = sc.GetAttribute("Method");
                    return string.IsNullOrWhiteSpace(method) ||
                           string.Equals(method, Saml2BearerMethod, StringComparison.Ordinal);
                })
                .Select(sc => GetDirectChildElement(sc, "SubjectConfirmationData", Saml2AssertionNamespace))
                .FirstOrDefault(scd => scd != null);

            var recipient = confirmationData?.GetAttribute("Recipient")?.Trim();
            if (string.IsNullOrWhiteSpace(recipient) ||
                !string.Equals(recipient, expectedRecipient, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("[Enterprise SSO] SAML Recipient mismatch for tenant '{TenantId}'. Expected '{Expected}', got '{Actual}'.",
                    tenantId, expectedRecipient, recipient);
                _auditLogger.LogAccessDenied(nameId, "SamlRecipientMismatch", tenantId, "Recipient mismatch");
                throw new SecurityTokenValidationException("SAML assertion recipient mismatch.");
            }

            var scNotOnOrAfterStr = confirmationData?.GetAttribute("NotOnOrAfter");
            if (!string.IsNullOrWhiteSpace(scNotOnOrAfterStr))
            {
                if (!TryParseUtcTimestamp(scNotOnOrAfterStr, out var scNotOnOrAfter) || utcNow >= scNotOnOrAfter.Add(ClockSkew))
                {
                    _auditLogger.LogAccessDenied(nameId, "SamlTokenExpired", tenantId, "SubjectConfirmationData expired");
                    throw new SecurityTokenExpiredException("SAML subject confirmation has expired.");
                }
            }

            // 6. Extract Attributes & Enforce Mandatory Tenant Attribute
            var identity = new ClaimsIdentity("EnterpriseSAML2");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, nameId));
            identity.AddClaim(new Claim(ClaimTypes.Name, nameId));
            identity.AddClaim(new Claim("tid", tenantId));
            identity.AddClaim(new Claim("tenant_id", tenantId));
            identity.AddClaim(new Claim("idp_protocol", "SAML2"));

            string? assertionTenant = null;
            var attributeStatements = GetDirectChildElements(assertionElement, "AttributeStatement", Saml2AssertionNamespace);
            foreach (var stmt in attributeStatements)
            {
                foreach (var attr in GetDirectChildElements(stmt, "Attribute", Saml2AssertionNamespace))
                {
                    var attrName = attr.GetAttribute("Name");
                    var attrValue = GetDirectChildElement(attr, "AttributeValue", Saml2AssertionNamespace)?.InnerText?.Trim();
                    if (string.IsNullOrWhiteSpace(attrName) || string.IsNullOrWhiteSpace(attrValue))
                        continue;

                    if (attrName.EndsWith("tenantid", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(attrName, "tenant_id", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(attrName, "tid", StringComparison.OrdinalIgnoreCase))
                    {
                        assertionTenant = attrValue;
                    }
                    else if (attrName.EndsWith("emailaddress", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(attrName, "email", StringComparison.OrdinalIgnoreCase))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Email, attrValue));
                        identity.AddClaim(new Claim(ClaimTypes.Upn, attrValue));
                    }
                    else if (attrName.EndsWith("role", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(attrName, "role", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(attrName, "roles", StringComparison.OrdinalIgnoreCase))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Role, attrValue));
                    }
                    else if (!ReservedClaimTypes.Contains(attrName))
                    {
                        identity.AddClaim(new Claim(attrName, attrValue));
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(assertionTenant))
            {
                _logger.LogWarning("[Enterprise SSO] SAML assertion missing required tenant attribute for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied(nameId, "SamlMissingTenantAttribute", tenantId, "Missing required tenant attribute");
                throw new SecurityTokenValidationException("SAML assertion is missing required tenant attribute.");
            }

            if (!string.Equals(assertionTenant, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("[Security Alert] Cross-tenant SAML federation violation! Assertion Tenant='{AssertTenant}' vs Target='{TargetTenant}'.",
                    assertionTenant, tenantId);
                _auditLogger.LogCrossTenantAttempt(nameId, assertionTenant, "saml-federation", tenantId, "SamlAuthentication");
                throw new UnauthorizedAccessException($"Cross-tenant access prohibited: SAML tenant '{assertionTenant}' does not match target tenant '{tenantId}'.");
            }

            // 7. Enforce Replay Protection on Assertion ID
            var assertionId = assertionElement.GetAttribute("ID");
            if (string.IsNullOrWhiteSpace(assertionId))
            {
                _auditLogger.LogAccessDenied(nameId, "SamlMissingAssertionId", tenantId, "Missing Assertion ID");
                throw new SecurityTokenValidationException("SAML assertion is missing required ID attribute.");
            }

            if (!_replayCache.TryConsume(tenantId, assertionId, notOnOrAfter.Add(ClockSkew)))
            {
                _logger.LogWarning("[Security Alert] SAML assertion replay detected! AssertionId='{AssertionId}', Tenant='{TenantId}'.",
                    assertionId, tenantId);
                _auditLogger.LogAccessDenied(nameId, "SamlAssertionReplay", tenantId, "Assertion replay detected");
                throw new SecurityTokenReplayDetectedException("SAML assertion has already been redeemed.");
            }

            _logger.LogInformation("[Enterprise SSO] Successfully federated SAML identity: User='{User}', Tenant='{TenantId}'.", nameId, tenantId);
            _auditLogger.LogAccessGranted(nameId, "SamlAuthentication", tenantId);

            return new ClaimsPrincipal(identity);
        }

        public async Task<string> GenerateServiceSessionJwtAsync(ClaimsPrincipal enterprisePrincipal, string tenantId, CancellationToken ct = default)
        {
            var signingKeySecret = !string.IsNullOrWhiteSpace(_jwtOptions.SecretKey)
                ? _jwtOptions.SecretKey
                : await _hsmKeyVault.GetSecretAsync(_hsmOptions.JwtSecretKeyName, ct);

            var issuer = !string.IsNullOrWhiteSpace(_jwtOptions.Issuer)
                ? _jwtOptions.Issuer
                : "PersonalAIAssistant.Memory";
            var audience = !string.IsNullOrWhiteSpace(_jwtOptions.Audience)
                ? _jwtOptions.Audience
                : "PersonalAIAssistant.Memory.Api";

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
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: credentials);

            return _tokenHandler.WriteToken(token);
        }

        public string GenerateSpMetadataXml(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
                throw new ArgumentException("TenantId cannot be null or empty.", nameof(tenantId));

            var escapedTenantId = SecurityElement.Escape(Uri.EscapeDataString(tenantId.Trim()));
            var escapedSpEntityId = SecurityElement.Escape(_options.SpEntityId);
            var escapedAcsUrl = SecurityElement.Escape(_options.AssertionConsumerServiceUrl);

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<md:EntityDescriptor xmlns:md=""urn:oasis:names:tc:SAML:2.0:metadata"" entityID=""{escapedSpEntityId}/{escapedTenantId}"">
  <md:SPSSODescriptor AuthnRequestsSigned=""true"" WantAssertionsSigned=""true"" protocolSupportEnumeration=""urn:oasis:names:tc:SAML:2.0:protocol"">
    <md:NameIDFormat>urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress</md:NameIDFormat>
    <md:AssertionConsumerService Binding=""urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST"" Location=""{escapedAcsUrl}"" index=""1"" isDefault=""true""/>
  </md:SPSSODescriptor>
</md:EntityDescriptor>";
        }

        private async Task<IReadOnlyCollection<SecurityKey>> ResolveOidcSigningKeysAsync(
            EnterpriseIdpConfig config,
            string tenantId,
            CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(config.SigningPublicKeyXmlOrPem))
            {
                return ParseConfiguredOidcKeys(config);
            }

            var metadataAddress = !string.IsNullOrWhiteSpace(config.IdpMetadataUrl)
                ? config.IdpMetadataUrl.Trim()
                : !string.IsNullOrWhiteSpace(config.Authority)
                    ? $"{config.Authority.TrimEnd('/')}/.well-known/openid-configuration"
                    : null;

            if (string.IsNullOrWhiteSpace(metadataAddress))
            {
                return Array.Empty<SecurityKey>();
            }

            if (!Uri.TryCreate(metadataAddress, UriKind.Absolute, out var metadataUri) ||
                !string.Equals(metadataUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("[Enterprise SSO] Rejected non-HTTPS OIDC metadata URL '{Url}' for tenant '{TenantId}'.",
                    metadataAddress, tenantId);
                throw new SecurityTokenException("OIDC metadata URL must use HTTPS.");
            }

            var manager = _oidcConfigManagers.GetOrAdd(metadataAddress, url =>
            {
                var httpClient = _httpClientFactory?.CreateClient("oidc-discovery") ?? new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(15)
                };
                var retriever = new HttpDocumentRetriever(httpClient) { RequireHttps = true };
                return new ConfigurationManager<OpenIdConnectConfiguration>(
                    url,
                    new OpenIdConnectConfigurationRetriever(),
                    retriever);
            });

            var oidcConfig = await manager.GetConfigurationAsync(ct);
            return oidcConfig.SigningKeys.ToList();
        }

        private static IReadOnlyCollection<SecurityKey> ParseConfiguredOidcKeys(EnterpriseIdpConfig config)
        {
            var raw = config.SigningPublicKeyXmlOrPem!.Trim();
            var keys = new List<SecurityKey>();

            if (raw.StartsWith("{", StringComparison.Ordinal))
            {
                var jwks = new JsonWebKeySet(raw);
                if (jwks.Keys.Count > 0)
                {
                    keys.AddRange(jwks.GetSigningKeys());
                }
                else
                {
                    keys.Add(new JsonWebKey(raw));
                }
                return keys;
            }

            if (TryParseCertificate(raw, out var cert))
            {
                ValidateCertificateThumbprintAndLifetime(cert!, config.SigningCertThumbprint);
                keys.Add(new X509SecurityKey(cert!));
                return keys;
            }

            if (raw.Contains("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var rsa = RSA.Create();
                    rsa.ImportFromPem(raw);
                    keys.Add(new RsaSecurityKey(rsa));
                    return keys;
                }
                catch (CryptographicException)
                {
                    var ecdsa = ECDsa.Create();
                    ecdsa.ImportFromPem(raw);
                    keys.Add(new ECDsaSecurityKey(ecdsa));
                    return keys;
                }
            }

            if (raw.StartsWith("<", StringComparison.Ordinal))
            {
                var rsa = RSA.Create();
                rsa.FromXmlString(raw);
                keys.Add(new RsaSecurityKey(rsa));
                return keys;
            }

            throw new SecurityTokenException("Unsupported OIDC signing public key format.");
        }

        private static XmlDocument ParseXmlSafely(string rawXml)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxSamlPayloadLength,
                MaxCharactersFromEntities = 1024
            };

            using var stringReader = new StringReader(rawXml);
            using var xmlReader = XmlReader.Create(stringReader, settings);

            var doc = new XmlDocument
            {
                PreserveWhitespace = true,
                XmlResolver = null
            };
            doc.Load(xmlReader);
            return doc;
        }

        private (XmlElement? Response, XmlElement Assertion) ExtractValidatedSamlElements(XmlDocument doc, string tenantId)
        {
            var root = doc.DocumentElement
                ?? throw new SecurityTokenException("SAML XML has no root element.");

            var allAssertions = CountAssertions(doc);
            if (allAssertions != 1)
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlInvalidStructure", tenantId, $"Expected 1 Assertion, found {allAssertions}");
                throw new SecurityTokenException("SAML payload must contain exactly one Assertion element.");
            }

            if (IsSamlElement(root, "Assertion", Saml2AssertionNamespace))
            {
                return (null, root);
            }

            if (IsSamlElement(root, "Response", Saml2ProtocolNamespace))
            {
                var assertion = GetDirectChildElement(root, "Assertion", Saml2AssertionNamespace);
                if (assertion == null)
                {
                    _auditLogger.LogAccessDenied("saml-user", "SamlInvalidStructure", tenantId, "Assertion is not a direct child of Response");
                    throw new SecurityTokenException("SAML Assertion must be a direct child of Response.");
                }
                return (root, assertion);
            }

            _auditLogger.LogAccessDenied("saml-user", "SamlInvalidStructure", tenantId, "Unexpected SAML root element");
            throw new SecurityTokenException("Unexpected SAML 2.0 root element.");
        }

        private void VerifySamlXmlSignature(
            XmlDocument doc,
            XmlElement? responseElement,
            XmlElement assertionElement,
            EnterpriseIdpConfig config,
            string tenantId)
        {
            var assertionSignature = GetDirectChildElement(assertionElement, "Signature", XmlDsigNamespace);
            var responseSignature = responseElement != null
                ? GetDirectChildElement(responseElement, "Signature", XmlDsigNamespace)
                : null;

            XmlElement signatureElement;
            XmlElement signedTargetElement;

            if (assertionSignature != null)
            {
                signatureElement = assertionSignature;
                signedTargetElement = assertionElement;
            }
            else if (responseSignature != null && responseElement != null)
            {
                signatureElement = responseSignature;
                signedTargetElement = responseElement;
            }
            else
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlMissingSignature", tenantId, "Missing XML signature");
                throw new SecurityTokenInvalidSignatureException("SAML assertion must be cryptographically signed.");
            }

            var targetId = signedTargetElement.GetAttribute("ID");
            if (string.IsNullOrWhiteSpace(targetId))
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlInvalidSignature", tenantId, "Signed element missing ID attribute");
                throw new SecurityTokenInvalidSignatureException("Signed SAML element is missing required ID attribute.");
            }

            var signedXml = new SamlSignedXml(doc);
            try
            {
                signedXml.LoadXml(signatureElement);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Enterprise SSO] Failed to parse XML signature for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied("saml-user", "SamlInvalidSignature", tenantId, "Malformed XML signature");
                throw new SecurityTokenInvalidSignatureException("Malformed SAML XML signature.", ex);
            }

            ValidateSignatureReferences(signedXml, signedTargetElement == doc.DocumentElement, targetId, tenantId);

            bool signatureValid;
            try
            {
                signatureValid = CheckSamlSignatureWithTrustedKey(signedXml, signatureElement, config);
            }
            catch (SecurityTokenException)
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlInvalidSignature", tenantId, "Trusted SAML key check failed");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Enterprise SSO] Error verifying SAML signature for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied("saml-user", "SamlInvalidSignature", tenantId, "Signature verification error");
                throw new SecurityTokenInvalidSignatureException("SAML assertion signature verification failed.", ex);
            }

            if (!signatureValid)
            {
                _logger.LogError("[Security Alert] Invalid SAML XML signature detected for tenant '{TenantId}'.", tenantId);
                _auditLogger.LogAccessDenied("saml-user", "SamlInvalidSignature", tenantId, "Signature verification returned false");
                throw new SecurityTokenInvalidSignatureException("SAML assertion signature verification failed.");
            }
        }

        private void ValidateSignatureReferences(SamlSignedXml signedXml, bool isRootElement, string targetId, string tenantId)
        {
            var references = signedXml.SignedInfo?.References;
            if (references == null || references.Count == 0)
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlInvalidSignature", tenantId, "No signature references");
                throw new SecurityTokenInvalidSignatureException("SAML XML signature contains no references.");
            }

            var expectedRefUri = "#" + targetId;
            var coversTarget = false;

            foreach (Reference reference in references)
            {
                var uri = reference.Uri ?? string.Empty;
                if (string.Equals(uri, expectedRefUri, StringComparison.Ordinal) ||
                    (isRootElement && string.IsNullOrEmpty(uri)))
                {
                    coversTarget = true;
                }
                else
                {
                    _auditLogger.LogAccessDenied("saml-user", "SamlInvalidSignature", tenantId, $"Unexpected reference URI '{uri}'");
                    throw new SecurityTokenInvalidSignatureException("SAML XML signature references an unexpected element.");
                }

                foreach (Transform transform in reference.TransformChain)
                {
                    if (string.IsNullOrEmpty(transform.Algorithm) || !AllowedXmlTransforms.Contains(transform.Algorithm))
                    {
                        _auditLogger.LogAccessDenied("saml-user", "SamlInvalidSignature", tenantId, $"Disallowed XML transform '{transform.Algorithm}'");
                        throw new SecurityTokenInvalidSignatureException("SAML XML signature uses a disallowed transform.");
                    }
                }
            }

            if (!coversTarget)
            {
                _auditLogger.LogAccessDenied("saml-user", "SamlInvalidSignature", tenantId, "Signature does not reference target element");
                throw new SecurityTokenInvalidSignatureException("SAML XML signature does not cover the target element.");
            }
        }

        private static bool CheckSamlSignatureWithTrustedKey(
            SamlSignedXml signedXml,
            XmlElement signatureElement,
            EnterpriseIdpConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.SigningPublicKeyXmlOrPem))
            {
                var raw = config.SigningPublicKeyXmlOrPem.Trim();

                if (TryParseCertificate(raw, out var cert))
                {
                    using (cert)
                    {
                        ValidateCertificateThumbprintAndLifetime(cert!, config.SigningCertThumbprint);
                        return signedXml.CheckSignature(cert!, verifySignatureOnly: true);
                    }
                }

                if (raw.Contains("BEGIN", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var rsa = RSA.Create();
                        rsa.ImportFromPem(raw);
                        return signedXml.CheckSignature(rsa);
                    }
                    catch (CryptographicException)
                    {
                        using var ecdsa = ECDsa.Create();
                        ecdsa.ImportFromPem(raw);
                        return signedXml.CheckSignature(ecdsa);
                    }
                }

                if (raw.StartsWith("<", StringComparison.Ordinal))
                {
                    using var rsa = RSA.Create();
                    rsa.FromXmlString(raw);
                    return signedXml.CheckSignature(rsa);
                }

                throw new SecurityTokenException("Unsupported SAML signing public key format.");
            }

            if (!string.IsNullOrWhiteSpace(config.SigningCertThumbprint))
            {
                var certElement = signatureElement
                    .GetElementsByTagName("X509Certificate", XmlDsigNamespace)
                    .Cast<XmlElement>()
                    .FirstOrDefault();

                if (certElement == null || string.IsNullOrWhiteSpace(certElement.InnerText))
                {
                    throw new SecurityTokenInvalidSignatureException("SAML signature does not contain an X509Certificate to match configured thumbprint.");
                }

                using var embeddedCert = new X509Certificate2(Convert.FromBase64String(certElement.InnerText.Trim()));
                ValidateCertificateThumbprintAndLifetime(embeddedCert, config.SigningCertThumbprint);
                return signedXml.CheckSignature(embeddedCert, verifySignatureOnly: true);
            }

            throw new SecurityTokenInvalidSignatureException("No trusted SAML signing key or certificate thumbprint is configured for tenant.");
        }

        private static bool TryParseCertificate(string raw, out X509Certificate2? certificate)
        {
            certificate = null;
            if (raw.Contains("BEGIN CERTIFICATE", StringComparison.OrdinalIgnoreCase))
            {
                certificate = X509Certificate2.CreateFromPem(raw);
                return true;
            }

            if (!raw.StartsWith("<", StringComparison.Ordinal) &&
                !raw.StartsWith("{", StringComparison.Ordinal) &&
                !raw.Contains("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var bytes = Convert.FromBase64String(raw);
                    certificate = new X509Certificate2(bytes);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static void ValidateCertificateThumbprintAndLifetime(X509Certificate2 cert, string? expectedThumbprint)
        {
            var now = DateTime.UtcNow;
            if (now < cert.NotBefore.ToUniversalTime() || now > cert.NotAfter.ToUniversalTime())
            {
                throw new SecurityTokenInvalidSigningKeyException("Configured SAML/OIDC signing certificate is outside its validity period.");
            }

            if (!string.IsNullOrWhiteSpace(expectedThumbprint))
            {
                var normalizedExpected = expectedThumbprint.Replace(":", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Trim();
                var sha256Thumbprint = cert.GetCertHashString(HashAlgorithmName.SHA256);
                var sha1Thumbprint = cert.Thumbprint;

                if (!string.Equals(normalizedExpected, sha256Thumbprint, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(normalizedExpected, sha1Thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityTokenInvalidSigningKeyException("Signing certificate thumbprint does not match tenant configuration.");
                }
            }
        }

        private static int CountAssertions(XmlDocument doc)
        {
            var count = 0;
            var stack = new Stack<XmlNode>();
            if (doc.DocumentElement != null)
            {
                stack.Push(doc.DocumentElement);
            }

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node is XmlElement el && IsSamlElement(el, "Assertion", Saml2AssertionNamespace))
                {
                    count++;
                }

                foreach (XmlNode child in node.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element)
                    {
                        stack.Push(child);
                    }
                }
            }

            return count;
        }

        private static bool IsSamlElement(XmlElement element, string localName, string expectedNamespace)
        {
            return string.Equals(element.LocalName, localName, StringComparison.Ordinal) &&
                   (string.Equals(element.NamespaceURI, expectedNamespace, StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(element.NamespaceURI));
        }

        private static XmlElement? GetDirectChildElement(XmlElement parent, string localName, string expectedNamespace)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child is XmlElement el && IsSamlElement(el, localName, expectedNamespace))
                {
                    return el;
                }
            }
            return null;
        }

        private static IEnumerable<XmlElement> GetDirectChildElements(XmlElement parent, string localName, string expectedNamespace)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child is XmlElement el && IsSamlElement(el, localName, expectedNamespace))
                {
                    yield return el;
                }
            }
        }

        private static bool TryParseUtcTimestamp(string? value, out DateTimeOffset timestamp)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                timestamp = default;
                return false;
            }

            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out timestamp);
        }

        private sealed class SamlSignedXml : SignedXml
        {
            public SamlSignedXml(XmlDocument document) : base(document)
            {
            }

            public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
            {
                if (document?.DocumentElement == null || string.IsNullOrEmpty(idValue))
                    return null;

                XmlElement? match = null;
                var stack = new Stack<XmlElement>();
                stack.Push(document.DocumentElement);

                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    var attrVal = current.GetAttribute("ID");
                    if (string.IsNullOrEmpty(attrVal)) attrVal = current.GetAttribute("Id");
                    if (string.IsNullOrEmpty(attrVal)) attrVal = current.GetAttribute("id");

                    if (string.Equals(attrVal, idValue, StringComparison.Ordinal))
                    {
                        if (match != null)
                        {
                            throw new CryptographicException("Duplicate XML ID detected in SAML document.");
                        }
                        match = current;
                    }

                    foreach (XmlNode child in current.ChildNodes)
                    {
                        if (child is XmlElement childEl)
                        {
                            stack.Push(childEl);
                        }
                    }
                }

                return match;
            }
        }
    }
}
