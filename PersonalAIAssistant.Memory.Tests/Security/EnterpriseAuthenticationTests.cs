using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
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
    public class EnterpriseAuthenticationTests : IDisposable
    {
        private const string OidcAuthority = "https://login.microsoftonline.com/tenant-hdfc/v2.0";
        private const string OidcAudience = "api://personal-ai-assistant";
        private const string SamlAuthority = "https://sso.pingidentity.com/idp/tenant-icici";
        private const string SamlAudience = "https://memory.ai-assistant.internal/sp";
        private const string SamlAcsUrl = "https://memory.ai-assistant.internal/api/v1/auth/enterprise/saml";

        private readonly RSA _oidcRsa;
        private readonly RSA _samlRsa;
        private readonly X509Certificate2 _samlCert;
        private readonly SimulatedFipsHsmKeyVaultService _hsmService;
        private readonly Mock<ISecurityAuditLogger> _mockAuditLogger;
        private readonly EnterpriseSsoOptions _ssoOptions;
        private readonly InMemorySamlAssertionReplayCache _replayCache;
        private readonly EnterpriseIdentityProviderService _idpService;

        public EnterpriseAuthenticationTests()
        {
            _oidcRsa = RSA.Create(2048);
            _samlRsa = RSA.Create(2048);
            _samlCert = CreateSelfSignedCert(_samlRsa, "CN=PingIdentity-ICICI-IdP");

            var hsmOpts = new HsmOptions { Provider = "SimulatedHsm", FipsComplianceLevel = "FIPS 140-2 Level 3" };
            _hsmService = new SimulatedFipsHsmKeyVaultService(Options.Create(hsmOpts), NullLogger<SimulatedFipsHsmKeyVaultService>.Instance);
            _mockAuditLogger = new Mock<ISecurityAuditLogger>();
            _replayCache = new InMemorySamlAssertionReplayCache();

            _ssoOptions = new EnterpriseSsoOptions
            {
                Enabled = true,
                SpEntityId = SamlAudience,
                AssertionConsumerServiceUrl = SamlAcsUrl,
                Providers = new List<EnterpriseIdpConfig>
                {
                    new()
                    {
                        TenantId = "tenant-hdfc",
                        Domain = "hdfcbank.com",
                        Protocol = "OIDC",
                        Authority = OidcAuthority,
                        ClientId = OidcAudience,
                        Audience = OidcAudience,
                        SigningPublicKeyXmlOrPem = _oidcRsa.ExportSubjectPublicKeyInfoPem()
                    },
                    new()
                    {
                        TenantId = "tenant-icici",
                        Domain = "icicibank.com",
                        Protocol = "SAML2",
                        Authority = SamlAuthority,
                        ClientId = "personal-ai-assistant-sp",
                        Audience = SamlAudience,
                        SigningPublicKeyXmlOrPem = _samlCert.ExportCertificatePem(),
                        SigningCertThumbprint = _samlCert.GetCertHashString(HashAlgorithmName.SHA256)
                    }
                }
            };

            _idpService = new EnterpriseIdentityProviderService(
                Options.Create(_ssoOptions),
                _hsmService,
                _mockAuditLogger.Object,
                NullLogger<EnterpriseIdentityProviderService>.Instance,
                replayCache: _replayCache);
        }

        public void Dispose()
        {
            _samlCert.Dispose();
            _samlRsa.Dispose();
            _oidcRsa.Dispose();
            _hsmService.Dispose();
        }

        // ── OIDC Tests ───────────────────────────────────────────────────────

        [Fact]
        public async Task ValidateOidcToken_WithValidSignedToken_ReturnsNormalizedPrincipal()
        {
            var rawJwt = CreateSignedOidcJwt(
                _oidcRsa,
                issuer: OidcAuthority,
                audience: OidcAudience,
                claims: new[]
                {
                    new Claim("sub", "emp-001"),
                    new Claim("oid", "emp-001"),
                    new Claim(ClaimTypes.Upn, "alice@hdfcbank.com"),
                    new Claim("tid", "tenant-hdfc"),
                    new Claim("role", "BankManager")
                });

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
        public async Task ValidateOidcToken_WithForgedKey_ThrowsSecurityTokenException()
        {
            using var attackerRsa = RSA.Create(2048);
            var rawJwt = CreateSignedOidcJwt(
                attackerRsa,
                issuer: OidcAuthority,
                audience: OidcAudience,
                claims: new[]
                {
                    new Claim("sub", "emp-001"),
                    new Claim("tid", "tenant-hdfc")
                });

            await Assert.ThrowsAnyAsync<SecurityTokenException>(() =>
                _idpService.ValidateOidcTokenAsync(rawJwt, "tenant-hdfc"));
        }

        [Fact]
        public async Task ValidateOidcToken_WithUnsignedAlgNone_ThrowsSecurityTokenException()
        {
            var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
            var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            var payload = Base64UrlEncoder.Encode(
                $"{{\"sub\":\"emp-001\",\"tid\":\"tenant-hdfc\",\"iss\":\"{OidcAuthority}\",\"aud\":\"{OidcAudience}\",\"exp\":{exp}}}");
            var unsignedJwt = $"{header}.{payload}.";

            await Assert.ThrowsAnyAsync<SecurityTokenException>(() =>
                _idpService.ValidateOidcTokenAsync(unsignedJwt, "tenant-hdfc"));
        }

        [Fact]
        public async Task ValidateOidcToken_WithSymmetricAlgConfusion_ThrowsSecurityTokenException()
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("OidcTestSecretKeyForTesting32BytesLongString!"));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: OidcAuthority,
                audience: OidcAudience,
                claims: new[] { new Claim("sub", "emp-001"), new Claim("tid", "tenant-hdfc") },
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds);

            var rawJwt = new JwtSecurityTokenHandler().WriteToken(token);

            await Assert.ThrowsAnyAsync<SecurityTokenException>(() =>
                _idpService.ValidateOidcTokenAsync(rawJwt, "tenant-hdfc"));
        }

        [Fact]
        public async Task ValidateOidcToken_WithWrongIssuer_ThrowsSecurityTokenInvalidIssuerException()
        {
            var rawJwt = CreateSignedOidcJwt(
                _oidcRsa,
                issuer: "https://login.microsoftonline.com/evil-tenant/v2.0",
                audience: OidcAudience,
                claims: new[]
                {
                    new Claim("sub", "emp-001"),
                    new Claim("tid", "tenant-hdfc")
                });

            await Assert.ThrowsAsync<SecurityTokenInvalidIssuerException>(() =>
                _idpService.ValidateOidcTokenAsync(rawJwt, "tenant-hdfc"));
        }

        [Fact]
        public async Task ValidateOidcToken_WithMissingTenantClaim_ThrowsSecurityTokenValidationException()
        {
            var rawJwt = CreateSignedOidcJwt(
                _oidcRsa,
                issuer: OidcAuthority,
                audience: OidcAudience,
                claims: new[]
                {
                    new Claim("sub", "emp-001"),
                    new Claim(ClaimTypes.Upn, "alice@hdfcbank.com")
                });

            await Assert.ThrowsAsync<SecurityTokenValidationException>(() =>
                _idpService.ValidateOidcTokenAsync(rawJwt, "tenant-hdfc"));
        }

        [Fact]
        public async Task ValidateOidcToken_WithCrossTenantToken_ThrowsUnauthorizedAndLogsAudit()
        {
            var rawJwt = CreateSignedOidcJwt(
                _oidcRsa,
                issuer: OidcAuthority,
                audience: OidcAudience,
                claims: new[]
                {
                    new Claim("sub", "attacker-user"),
                    new Claim("tid", "tenant-rogue-bank")
                });

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _idpService.ValidateOidcTokenAsync(rawJwt, "tenant-hdfc"));

            _mockAuditLogger.Verify(a => a.LogCrossTenantAttempt("attacker-user", "tenant-rogue-bank", "oidc-federation", "tenant-hdfc", "OidcTokenValidation"), Times.Once);
        }

        [Fact]
        public async Task ValidateOidcToken_WithExpiredToken_ThrowsSecurityTokenExpiredException()
        {
            var rawJwt = CreateSignedOidcJwt(
                _oidcRsa,
                issuer: OidcAuthority,
                audience: OidcAudience,
                claims: new[] { new Claim("sub", "emp-002"), new Claim("tid", "tenant-hdfc") },
                expires: DateTime.UtcNow.AddMinutes(-5));

            await Assert.ThrowsAsync<SecurityTokenExpiredException>(() =>
                _idpService.ValidateOidcTokenAsync(rawJwt, "tenant-hdfc"));
        }

        [Fact]
        public async Task ValidateOidcToken_ViaOidcDiscoveryJwks_ValidatesSignature()
        {
            var kid = "test-kid-1";
            var rsaKey = new RsaSecurityKey(_oidcRsa) { KeyId = kid };
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(rsaKey);
            jwk.Kid = kid;
            jwk.Use = "sig";
            jwk.Alg = SecurityAlgorithms.RsaSha256;

            var discoveryJson = JsonSerializer.Serialize(new
            {
                issuer = OidcAuthority,
                jwks_uri = $"{OidcAuthority}/discovery/v2.0/keys"
            });
            var jwksJson = JsonSerializer.Serialize(new
            {
                keys = new[]
                {
                    new { kty = jwk.Kty, use = jwk.Use, kid = jwk.Kid, alg = jwk.Alg, n = jwk.N, e = jwk.E }
                }
            });

            var handler = new StubHttpMessageHandler(req =>
            {
                var uri = req.RequestUri!.ToString();
                var body = uri.EndsWith("openid-configuration") ? discoveryJson : jwksJson;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            });

            var factoryMock = new Mock<IHttpClientFactory>();
            factoryMock.Setup(f => f.CreateClient("oidc-discovery")).Returns(() => new HttpClient(handler));

            var options = new EnterpriseSsoOptions
            {
                Providers = new List<EnterpriseIdpConfig>
                {
                    new()
                    {
                        TenantId = "tenant-hdfc",
                        Protocol = "OIDC",
                        Authority = OidcAuthority,
                        Audience = OidcAudience
                    }
                }
            };

            var service = new EnterpriseIdentityProviderService(
                Options.Create(options),
                _hsmService,
                _mockAuditLogger.Object,
                NullLogger<EnterpriseIdentityProviderService>.Instance,
                httpClientFactory: factoryMock.Object);

            var validJwt = CreateSignedOidcJwt(
                _oidcRsa,
                issuer: OidcAuthority,
                audience: OidcAudience,
                claims: new[] { new Claim("sub", "emp-jwks"), new Claim("tid", "tenant-hdfc") },
                kid: kid);

            var principal = await service.ValidateOidcTokenAsync(validJwt, "tenant-hdfc");
            Assert.Equal("emp-jwks", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);

            using var attackerRsa = RSA.Create(2048);
            var forgedJwt = CreateSignedOidcJwt(
                attackerRsa,
                issuer: OidcAuthority,
                audience: OidcAudience,
                claims: new[] { new Claim("sub", "emp-jwks"), new Claim("tid", "tenant-hdfc") },
                kid: kid);

            await Assert.ThrowsAsync<SecurityTokenInvalidSignatureException>(() =>
                service.ValidateOidcTokenAsync(forgedJwt, "tenant-hdfc"));
        }

        // ── SAML 2.0 Tests ───────────────────────────────────────────────────

        [Fact]
        public async Task ValidateSamlAssertion_WithValidSignedSamlXml_ReturnsNormalizedPrincipal()
        {
            var samlXml = CreateSignedSamlResponse(
                _samlRsa,
                _samlCert,
                assertionId: "_assertion-12345",
                issuer: SamlAuthority,
                audience: SamlAudience,
                recipient: SamlAcsUrl,
                nameId: "bob@icicibank.com",
                tenantId: "tenant-icici");

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
        public async Task ValidateSamlAssertion_WithUnsignedXml_ThrowsSecurityTokenInvalidSignatureException()
        {
            var unsignedXml = CreateUnsignedSamlResponse(
                assertionId: "_assertion-unsigned",
                issuer: SamlAuthority,
                audience: SamlAudience,
                recipient: SamlAcsUrl,
                nameId: "bob@icicibank.com",
                tenantId: "tenant-icici");

            await Assert.ThrowsAsync<SecurityTokenInvalidSignatureException>(() =>
                _idpService.ValidateSamlAssertionAsync(unsignedXml, "tenant-icici"));
        }

        [Fact]
        public async Task ValidateSamlAssertion_WithTamperedClaim_ThrowsSecurityTokenInvalidSignatureException()
        {
            var signedXml = CreateSignedSamlResponse(
                _samlRsa,
                _samlCert,
                assertionId: "_assertion-tamper",
                issuer: SamlAuthority,
                audience: SamlAudience,
                recipient: SamlAcsUrl,
                nameId: "bob@icicibank.com",
                tenantId: "tenant-icici");

            var tamperedXml = signedXml.Replace("bob@icicibank.com", "admin@icicibank.com", StringComparison.Ordinal);

            await Assert.ThrowsAsync<SecurityTokenInvalidSignatureException>(() =>
                _idpService.ValidateSamlAssertionAsync(tamperedXml, "tenant-icici"));
        }

        [Fact]
        public async Task ValidateSamlAssertion_WithXmlSignatureWrapping_ThrowsSecurityTokenException()
        {
            var signedXml = CreateSignedSamlResponse(
                _samlRsa,
                _samlCert,
                assertionId: "_assertion-legit",
                issuer: SamlAuthority,
                audience: SamlAudience,
                recipient: SamlAcsUrl,
                nameId: "bob@icicibank.com",
                tenantId: "tenant-icici");

            // Inject a second forged Assertion element (XSW attack)
            var evilAssertion = $@"<saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion"" ID=""_assertion-evil"">
                <saml2:Issuer>{SamlAuthority}</saml2:Issuer>
                <saml2:Subject><saml2:NameID>evil@icicibank.com</saml2:NameID></saml2:Subject>
            </saml2:Assertion>";
            var xswXml = signedXml.Replace("</saml2p:Response>", evilAssertion + "</saml2p:Response>", StringComparison.Ordinal);

            await Assert.ThrowsAnyAsync<SecurityTokenException>(() =>
                _idpService.ValidateSamlAssertionAsync(xswXml, "tenant-icici"));
        }

        [Fact]
        public async Task ValidateSamlAssertion_WithDtdXxe_ThrowsSecurityTokenException()
        {
            var xxeXml = @"<?xml version=""1.0""?>
<!DOCTYPE foo [<!ENTITY xxe SYSTEM ""file:///etc/passwd"">]>
<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol"">&xxe;</saml2p:Response>";

            await Assert.ThrowsAnyAsync<SecurityTokenException>(() =>
                _idpService.ValidateSamlAssertionAsync(xxeXml, "tenant-icici"));
        }

        [Fact]
        public async Task ValidateSamlAssertion_WithIssuerMismatch_ThrowsSecurityTokenInvalidIssuerException()
        {
            var signedXml = CreateSignedSamlResponse(
                _samlRsa,
                _samlCert,
                assertionId: "_assertion-bad-issuer",
                issuer: "https://sso.evilbank.com/idp",
                audience: SamlAudience,
                recipient: SamlAcsUrl,
                nameId: "bob@icicibank.com",
                tenantId: "tenant-icici");

            await Assert.ThrowsAsync<SecurityTokenInvalidIssuerException>(() =>
                _idpService.ValidateSamlAssertionAsync(signedXml, "tenant-icici"));
        }

        [Fact]
        public async Task ValidateSamlAssertion_WithAudienceMismatch_ThrowsSecurityTokenInvalidAudienceException()
        {
            var signedXml = CreateSignedSamlResponse(
                _samlRsa,
                _samlCert,
                assertionId: "_assertion-bad-aud",
                issuer: SamlAuthority,
                audience: "https://other-sp.example.com",
                recipient: SamlAcsUrl,
                nameId: "bob@icicibank.com",
                tenantId: "tenant-icici");

            await Assert.ThrowsAsync<SecurityTokenInvalidAudienceException>(() =>
                _idpService.ValidateSamlAssertionAsync(signedXml, "tenant-icici"));
        }

        [Fact]
        public async Task ValidateSamlAssertion_WithRecipientMismatch_ThrowsSecurityTokenValidationException()
        {
            var signedXml = CreateSignedSamlResponse(
                _samlRsa,
                _samlCert,
                assertionId: "_assertion-bad-recipient",
                issuer: SamlAuthority,
                audience: SamlAudience,
                recipient: "https://evil.example.com/acs",
                nameId: "bob@icicibank.com",
                tenantId: "tenant-icici");

            await Assert.ThrowsAsync<SecurityTokenValidationException>(() =>
                _idpService.ValidateSamlAssertionAsync(signedXml, "tenant-icici"));
        }

        [Fact]
        public async Task ValidateSamlAssertion_WithMissingConditions_ThrowsSecurityTokenValidationException()
        {
            var signedXml = CreateSignedSamlResponse(
                _samlRsa,
                _samlCert,
                assertionId: "_assertion-no-cond",
                issuer: SamlAuthority,
                audience: SamlAudience,
                recipient: SamlAcsUrl,
                nameId: "bob@icicibank.com",
                tenantId: "tenant-icici",
                includeConditions: false);

            await Assert.ThrowsAsync<SecurityTokenValidationException>(() =>
                _idpService.ValidateSamlAssertionAsync(signedXml, "tenant-icici"));
        }

        [Fact]
        public async Task ValidateSamlAssertion_WithMissingTenantAttribute_ThrowsSecurityTokenValidationException()
        {
            var signedXml = CreateSignedSamlResponse(
                _samlRsa,
                _samlCert,
                assertionId: "_assertion-no-tenant",
                issuer: SamlAuthority,
                audience: SamlAudience,
                recipient: SamlAcsUrl,
                nameId: "bob@icicibank.com",
                tenantId: null);

            await Assert.ThrowsAsync<SecurityTokenValidationException>(() =>
                _idpService.ValidateSamlAssertionAsync(signedXml, "tenant-icici"));
        }

        [Fact]
        public async Task ValidateSamlAssertion_WithCrossTenantAssertion_ThrowsUnauthorizedAndLogsAudit()
        {
            var samlXml = CreateSignedSamlResponse(
                _samlRsa,
                _samlCert,
                assertionId: "_assertion-evil",
                issuer: SamlAuthority,
                audience: SamlAudience,
                recipient: SamlAcsUrl,
                nameId: "rogue@evilbank.com",
                tenantId: "tenant-attacker-bank");

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                _idpService.ValidateSamlAssertionAsync(samlXml, "tenant-icici"));

            _mockAuditLogger.Verify(a => a.LogCrossTenantAttempt("rogue@evilbank.com", "tenant-attacker-bank", "saml-federation", "tenant-icici", "SamlAuthentication"), Times.Once);
        }

        [Fact]
        public async Task ValidateSamlAssertion_WithReplay_ThrowsSecurityTokenReplayDetectedException()
        {
            var samlXml = CreateSignedSamlResponse(
                _samlRsa,
                _samlCert,
                assertionId: "_assertion-replay-1",
                issuer: SamlAuthority,
                audience: SamlAudience,
                recipient: SamlAcsUrl,
                nameId: "bob@icicibank.com",
                tenantId: "tenant-icici");

            var firstPrincipal = await _idpService.ValidateSamlAssertionAsync(samlXml, "tenant-icici");
            Assert.NotNull(firstPrincipal);

            await Assert.ThrowsAsync<SecurityTokenReplayDetectedException>(() =>
                _idpService.ValidateSamlAssertionAsync(samlXml, "tenant-icici"));
        }

        // ── Session JWT & Metadata Tests ─────────────────────────────────────

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
        public async Task GenerateServiceSessionJwt_UsesConfiguredJwtOptions_WhenProvided()
        {
            var jwtOpts = new JwtOptions
            {
                SecretKey = "ConfiguredProductionJwtSecretKeyAtLeast32Chars!",
                Issuer = "https://identity.bfsi-memory.bank.internal",
                Audience = "https://api.bfsi-memory.bank.internal"
            };

            var service = new EnterpriseIdentityProviderService(
                Options.Create(_ssoOptions),
                _hsmService,
                _mockAuditLogger.Object,
                NullLogger<EnterpriseIdentityProviderService>.Instance,
                jwtOptions: Options.Create(jwtOpts));

            var identity = new ClaimsIdentity("Test");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "emp-008"));
            var sessionJwt = await service.GenerateServiceSessionJwtAsync(new ClaimsPrincipal(identity), "tenant-hdfc");

            var handler = new JwtSecurityTokenHandler();
            var validated = handler.ValidateToken(sessionJwt, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOpts.SecretKey)),
                ValidateIssuer = true,
                ValidIssuer = jwtOpts.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOpts.Audience,
                ValidateLifetime = true
            }, out _);

            Assert.Equal("emp-008", validated.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        }

        [Fact]
        public void GenerateSpMetadataXml_ReturnsValidSaml2EntityDescriptor()
        {
            var xml = _idpService.GenerateSpMetadataXml("tenant-hdfc");
            Assert.Contains("EntityDescriptor", xml);
            Assert.Contains("AssertionConsumerService", xml);
            Assert.Contains("tenant-hdfc", xml);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        internal static string CreateSignedOidcJwt(
            RSA rsa,
            string issuer,
            string audience,
            IEnumerable<Claim> claims,
            DateTime? expires = null,
            string? kid = null)
        {
            var effectiveExpires = expires ?? DateTime.UtcNow.AddHours(1);
            var key = new RsaSecurityKey(rsa) { KeyId = kid };
            var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: effectiveExpires.AddMinutes(-10),
                expires: effectiveExpires,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        internal static string CreateUnsignedSamlResponse(
            string assertionId,
            string issuer,
            string audience,
            string recipient,
            string nameId,
            string? tenantId,
            bool includeConditions = true)
        {
            var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O");
            var notOnOrAfter = DateTimeOffset.UtcNow.AddMinutes(30).ToString("O");

            var conditionsXml = includeConditions
                ? $@"<saml2:Conditions NotBefore=""{notBefore}"" NotOnOrAfter=""{notOnOrAfter}"">
      <saml2:AudienceRestriction>
        <saml2:Audience>{audience}</saml2:Audience>
      </saml2:AudienceRestriction>
    </saml2:Conditions>"
                : string.Empty;

            var tenantAttrXml = tenantId != null
                ? $@"<saml2:Attribute Name=""tenant_id"">
        <saml2:AttributeValue>{tenantId}</saml2:AttributeValue>
      </saml2:Attribute>"
                : string.Empty;

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<saml2p:Response xmlns:saml2p=""urn:oasis:names:tc:SAML:2.0:protocol"" ID=""_resp-{assertionId}"">
  <saml2:Assertion xmlns:saml2=""urn:oasis:names:tc:SAML:2.0:assertion"" ID=""{assertionId}"">
    <saml2:Issuer>{issuer}</saml2:Issuer>
    <saml2:Subject>
      <saml2:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">{nameId}</saml2:NameID>
      <saml2:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer"">
        <saml2:SubjectConfirmationData Recipient=""{recipient}"" NotOnOrAfter=""{notOnOrAfter}""/>
      </saml2:SubjectConfirmation>
    </saml2:Subject>
    {conditionsXml}
    <saml2:AttributeStatement>
      {tenantAttrXml}
      <saml2:Attribute Name=""http://schemas.microsoft.com/identity/claims/role"">
        <saml2:AttributeValue>WealthAdvisor</saml2:AttributeValue>
      </saml2:Attribute>
    </saml2:AttributeStatement>
  </saml2:Assertion>
</saml2p:Response>";
        }

        internal static string CreateSignedSamlResponse(
            RSA rsa,
            X509Certificate2 cert,
            string assertionId,
            string issuer,
            string audience,
            string recipient,
            string nameId,
            string? tenantId,
            bool includeConditions = true)
        {
            var rawXml = CreateUnsignedSamlResponse(assertionId, issuer, audience, recipient, nameId, tenantId, includeConditions);
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(rawXml);

            var assertion = (XmlElement)doc.GetElementsByTagName("Assertion", "urn:oasis:names:tc:SAML:2.0:assertion")[0]!;
            var signedXml = new SignedXml(doc)
            {
                SigningKey = rsa
            };
            signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;

            var reference = new Reference { Uri = "#" + assertionId };
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigExcC14NTransform());
            signedXml.AddReference(reference);

            var keyInfo = new KeyInfo();
            keyInfo.AddClause(new KeyInfoX509Data(cert));
            signedXml.KeyInfo = keyInfo;

            signedXml.ComputeSignature();
            var xmlSignature = signedXml.GetXml();

            var issuerNode = assertion.GetElementsByTagName("Issuer", "urn:oasis:names:tc:SAML:2.0:assertion")[0]!;
            assertion.InsertAfter(doc.ImportNode(xmlSignature, true), issuerNode);

            return doc.OuterXml;
        }

        internal static X509Certificate2 CreateSelfSignedCert(RSA rsa, string subjectName)
        {
            var req = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

            public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(_responder(request));
        }
    }
}
