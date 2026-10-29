using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models.Security;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class EnterpriseAuthenticationTests
    {
        private readonly EnterpriseIdentityProviderService _idpService;
        private readonly SimulatedFipsHsmKeyVaultService _hsmService;
        private readonly Mock<ISecurityAuditLogger> _mockAuditLogger;
        private readonly EnterpriseSsoOptions _ssoOptions;

        public EnterpriseAuthenticationTests()
        {
            var hsmOpts = new HsmOptions { Provider = "SimulatedHsm", FipsComplianceLevel = "FIPS 140-2 Level 3" };
            _hsmService = new SimulatedFipsHsmKeyVaultService(Options.Create(hsmOpts), NullLogger<SimulatedFipsHsmKeyVaultService>.Instance);
            _mockAuditLogger = new Mock<ISecurityAuditLogger>();

            _ssoOptions = new EnterpriseSsoOptions
            {
                Enabled = true,
                Providers = new List<EnterpriseIdpConfig>
                {
                    new()
                    {
                        TenantId = "tenant-hdfc",
                        Domain = "hdfcbank.com",
                        Protocol = "OIDC",
                        Authority = "https://login.microsoftonline.com/tenant-hdfc/v2.0",
                        ClientId = "api://personal-ai-assistant",
                        Audience = "api://personal-ai-assistant"
                    },
                    new()
                    {
                        TenantId = "tenant-icici",
                        Domain = "icicibank.com",
                        Protocol = "SAML2",
                        Authority = "https://sso.pingidentity.com/idp/tenant-icici",
                        ClientId = "personal-ai-assistant-sp",
                        Audience = "https://memory.ai-assistant.internal/sp"
                    }
                }
            };

            _idpService = new EnterpriseIdentityProviderService(
                Options.Create(_ssoOptions),
                _hsmService,
                _mockAuditLogger.Object,
                NullLogger<EnterpriseIdentityProviderService>.Instance);
        }

        [Fact]
        public async Task ValidateOidcToken_WithValidToken_ReturnsNormalizedPrincipal()
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("OidcTestSecretKeyForTesting32BytesLongString!"));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: "https://login.microsoftonline.com/tenant-hdfc/v2.0",
                audience: "api://personal-ai-assistant",
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "emp-001"),
                    new Claim("oid", "emp-001"),
                    new Claim(ClaimTypes.Upn, "alice@hdfcbank.com"),
                    new Claim("tid", "tenant-hdfc"),
                    new Claim(ClaimTypes.Role, "BankManager")
                },
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: credentials);

            var rawJwt = tokenHandler.WriteToken(token);

            var principal = await _idpService.ValidateOidcTokenAsync(rawJwt, "tenant-hdfc");

            Assert.NotNull(principal);
            Assert.Equal("emp-001", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            Assert.Equal("alice@hdfcbank.com", principal.FindFirst(ClaimTypes.Upn)?.Value);
            Assert.Equal("tenant-hdfc", principal.FindFirst("tid")?.Value);
            Assert.Equal("tenant-hdfc", principal.FindFirst("tenant_id")?.Value);
            Assert.Equal("OIDC", principal.FindFirst("idp_protocol")?.Value);
            Assert.Equal("BankManager", principal.FindFirst(ClaimTypes.Role)?.Value);

            _mockAuditLogger.Verify(a => a.LogAccessGranted("emp-001", "OidcAuthentication", "tenant-hdfc"), Times.Once);
        }

        [Fact]
        public async Task ValidateOidcToken_WithCrossTenantToken_ThrowsUnauthorizedAndLogsAudit()
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("OidcTestSecretKeyForTesting32BytesLongString!"));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Attacker possesses a token for tenant-rogue-bank, but attempts to access tenant-hdfc
            var token = new JwtSecurityToken(
                issuer: "https://login.microsoftonline.com/tenant-rogue/v2.0",
                audience: "api://personal-ai-assistant",
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "attacker-user"),
                    new Claim("tid", "tenant-rogue-bank")
                },
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: credentials);

            var rawJwt = tokenHandler.WriteToken(token);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _idpService.ValidateOidcTokenAsync(rawJwt, "tenant-hdfc"));

            _mockAuditLogger.Verify(a => a.LogCrossTenantAttempt("attacker-user", "tenant-rogue-bank", "oidc-federation", "tenant-hdfc", "OidcTokenValidation"), Times.Once);
        }

        [Fact]
        public async Task ValidateOidcToken_WithExpiredToken_ThrowsSecurityTokenExpiredException()
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("OidcTestSecretKeyForTesting32BytesLongString!"));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: "https://login.microsoftonline.com/tenant-hdfc/v2.0",
                audience: "api://personal-ai-assistant",
                claims: new[] { new Claim(ClaimTypes.NameIdentifier, "emp-002"), new Claim("tid", "tenant-hdfc") },
                expires: DateTime.UtcNow.AddMinutes(-5), // Expired!
                signingCredentials: credentials);

            var rawJwt = tokenHandler.WriteToken(token);

            await Assert.ThrowsAsync<SecurityTokenExpiredException>(() =>
                _idpService.ValidateOidcTokenAsync(rawJwt, "tenant-hdfc"));
        }

        [Fact]
        public async Task ValidateSamlAssertion_WithValidSamlXml_ReturnsNormalizedPrincipal()
        {
            var samlXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol"">
  <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion"" ID=""_assertion-12345"">
    <saml2:Issuer>https://sso.pingidentity.com/idp/tenant-icici</saml2:Issuer>
    <saml2:Subject>
      <saml2:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">bob@icicibank.com</saml2:NameID>
    </saml2:Subject>
    <saml2:Conditions NotBefore=""2026-01-01T00:00:00Z"" NotOnOrAfter=""2027-01-01T00:00:00Z""/>
    <saml2:AttributeStatement>
      <saml2:Attribute Name=""tenant_id"">
        <saml2:AttributeValue>tenant-icici</saml2:AttributeValue>
      </saml2:Attribute>
      <saml2:Attribute Name=""http://schemas.microsoft.com/identity/claims/role"">
        <saml2:AttributeValue>WealthAdvisor</saml2:AttributeValue>
      </saml2:Attribute>
    </saml2:AttributeStatement>
  </saml2:Assertion>
</saml2p:Response>";

            var principal = await _idpService.ValidateSamlAssertionAsync(samlXml, "tenant-icici");

            Assert.NotNull(principal);
            Assert.Equal("bob@icicibank.com", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            Assert.Equal("tenant-icici", principal.FindFirst("tid")?.Value);
            Assert.Equal("tenant-icici", principal.FindFirst("tenant_id")?.Value);
            Assert.Equal("SAML2", principal.FindFirst("idp_protocol")?.Value);
            Assert.Equal("WealthAdvisor", principal.FindFirst(ClaimTypes.Role)?.Value);

            _mockAuditLogger.Verify(a => a.LogAccessGranted("bob@icicibank.com", "SamlAuthentication", "tenant-icici"), Times.Once);
        }

        [Fact]
        public async Task ValidateSamlAssertion_WithCrossTenantAssertion_ThrowsUnauthorizedAndLogsAudit()
        {
            var samlXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol"">
  <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion"" ID=""_assertion-evil"">
    <saml2:Issuer>https://sso.evilbank.com/idp</saml2:Issuer>
    <saml2:Subject>
      <saml2:NameID>rogue@evilbank.com</saml2:NameID>
    </saml2:Subject>
    <saml2:AttributeStatement>
      <saml2:Attribute Name=""tenant_id"">
        <saml2:AttributeValue>tenant-attacker-bank</saml2:AttributeValue>
      </saml2:Attribute>
    </saml2:AttributeStatement>
  </saml2:Assertion>
</saml2p:Response>";

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _idpService.ValidateSamlAssertionAsync(samlXml, "tenant-icici"));

            _mockAuditLogger.Verify(a => a.LogCrossTenantAttempt("rogue@evilbank.com", "tenant-attacker-bank", "saml-federation", "tenant-icici", "SamlAuthentication"), Times.Once);
        }

        [Fact]
        public async Task GenerateServiceSessionJwt_ProducesValidJwt_WithHsmBacking()
        {
            var identity = new ClaimsIdentity("Test");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "emp-007"));
            identity.AddClaim(new Claim(ClaimTypes.Upn, "james@bank.com"));
            identity.AddClaim(new Claim("idp_protocol", "OIDC"));
            identity.AddClaim(new Claim(ClaimTypes.Role, "SecurityAdmin"));
            var principal = new ClaimsPrincipal(identity);

            var sessionJwt = await _idpService.GenerateServiceSessionJwtAsync(principal, "tenant-hdfc");

            Assert.False(string.IsNullOrWhiteSpace(sessionJwt));

            var handler = new JwtSecurityTokenHandler();
            var parsed = handler.ReadJwtToken(sessionJwt);

            Assert.Equal("PersonalAIAssistant.Memory", parsed.Issuer);
            Assert.Contains("PersonalAIAssistant.Memory.Api", parsed.Audiences);
            Assert.Equal("emp-007", parsed.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
            Assert.Equal("tenant-hdfc", parsed.Claims.First(c => c.Type == "tid").Value);
            Assert.Equal("true", parsed.Claims.First(c => c.Type == "fips_hsm_secured").Value);
        }

        [Fact]
        public void GenerateSpMetadataXml_ReturnsValidSaml2EntityDescriptor()
        {
            var xml = _idpService.GenerateSpMetadataXml("tenant-hdfc");
            Assert.Contains("EntityDescriptor", xml);
            Assert.Contains("AssertionConsumerService", xml);
            Assert.Contains("tenant-hdfc", xml);
        }
    }
}
