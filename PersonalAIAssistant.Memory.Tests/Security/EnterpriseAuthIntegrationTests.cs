using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class EnterpriseAuthIntegrationTests : IDisposable
    {
        private const string JwtSecret = "ProductionAlignedJwtSigningKeyAtLeast32CharsLong!";
        private const string JwtIssuer = "https://identity.bfsi-memory.bank.internal";
        private const string JwtAudience = "https://api.bfsi-memory.bank.internal";

        private const string OidcAuthority = "https://login.microsoftonline.com/tenant-hdfc/v2.0";
        private const string OidcAudience = "api://personal-ai-assistant";
        private const string SamlAuthority = "https://sso.pingidentity.com/idp/tenant-icici";
        private const string SamlAudience = "https://memory.ai-assistant.internal/sp";
        private const string SamlAcsUrl = "https://memory.ai-assistant.internal/api/v1/auth/enterprise/saml";

        private readonly RSA _oidcRsa;
        private readonly RSA _samlRsa;
        private readonly X509Certificate2 _samlCert;
        private readonly WebApplicationFactory<Program> _factory;

        public EnterpriseAuthIntegrationTests()
        {
            _oidcRsa = RSA.Create(2048);
            _samlRsa = RSA.Create(2048);
            _samlCert = EnterpriseAuthenticationTests.CreateSelfSignedCert(_samlRsa, "CN=PingIdentity-ICICI-IdP");

            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("UseInMemoryStore", "true");
                builder.UseSetting("Jwt:SecretKey", JwtSecret);
                builder.UseSetting("Jwt:Issuer", JwtIssuer);
                builder.UseSetting("Jwt:Audience", JwtAudience);

                builder.UseSetting("EnterpriseSso:SpEntityId", SamlAudience);
                builder.UseSetting("EnterpriseSso:AssertionConsumerServiceUrl", SamlAcsUrl);

                builder.UseSetting("EnterpriseSso:Providers:0:TenantId", "tenant-hdfc");
                builder.UseSetting("EnterpriseSso:Providers:0:Domain", "hdfcbank.com");
                builder.UseSetting("EnterpriseSso:Providers:0:Protocol", "OIDC");
                builder.UseSetting("EnterpriseSso:Providers:0:Authority", OidcAuthority);
                builder.UseSetting("EnterpriseSso:Providers:0:Audience", OidcAudience);
                builder.UseSetting("EnterpriseSso:Providers:0:SigningPublicKeyXmlOrPem", _oidcRsa.ExportSubjectPublicKeyInfoPem());

                builder.UseSetting("EnterpriseSso:Providers:1:TenantId", "tenant-icici");
                builder.UseSetting("EnterpriseSso:Providers:1:Domain", "icicibank.com");
                builder.UseSetting("EnterpriseSso:Providers:1:Protocol", "SAML2");
                builder.UseSetting("EnterpriseSso:Providers:1:Authority", SamlAuthority);
                builder.UseSetting("EnterpriseSso:Providers:1:Audience", SamlAudience);
                builder.UseSetting("EnterpriseSso:Providers:1:SigningPublicKeyXmlOrPem", _samlCert.ExportCertificatePem());
                builder.UseSetting("EnterpriseSso:Providers:1:SigningCertThumbprint", _samlCert.GetCertHashString(HashAlgorithmName.SHA256));
            });
        }

        public void Dispose()
        {
            _factory.Dispose();
            _samlCert.Dispose();
            _samlRsa.Dispose();
            _oidcRsa.Dispose();
        }

        [Fact]
        public async Task OidcExchange_WithValidToken_MintsBearerTokenAcceptedByMcpAndApi()
        {
            var client = _factory.CreateClient();
            var idToken = EnterpriseAuthenticationTests.CreateSignedOidcJwt(
                _oidcRsa,
                issuer: OidcAuthority,
                audience: OidcAudience,
                claims: new[]
                {
                    new Claim("sub", "emp-sso-oidc"),
                    new Claim("tid", "tenant-hdfc"),
                    new Claim("role", "Employee")
                });

            var exchangeResp = await client.PostAsJsonAsync("/api/v1/auth/enterprise/oidc", new
            {
                idToken,
                tenantId = "tenant-hdfc"
            });

            exchangeResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var payload = await exchangeResp.Content.ReadFromJsonAsync<JsonElement>();
            var accessToken = payload.GetProperty("accessToken").GetString();
            accessToken.Should().NotBeNullOrWhiteSpace();

            // Verify the minted SSO bearer token is accepted by /mcp (memory tools)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var mcpResp = await client.PostAsJsonAsync("/mcp", new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list"
            });

            mcpResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task SamlExchange_WithValidAssertion_MintsBearerTokenAcceptedByMcp_AndRejectsReplay()
        {
            var client = _factory.CreateClient();
            var samlXml = EnterpriseAuthenticationTests.CreateSignedSamlResponse(
                _samlRsa,
                _samlCert,
                assertionId: "_integ-saml-1",
                issuer: SamlAuthority,
                audience: SamlAudience,
                recipient: SamlAcsUrl,
                nameId: "bob@icicibank.com",
                tenantId: "tenant-icici");

            var exchangeResp = await client.PostAsJsonAsync("/api/v1/auth/enterprise/saml", new
            {
                samlAssertionXml = samlXml,
                tenantId = "tenant-icici"
            });

            exchangeResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var payload = await exchangeResp.Content.ReadFromJsonAsync<JsonElement>();
            var accessToken = payload.GetProperty("accessToken").GetString();
            accessToken.Should().NotBeNullOrWhiteSpace();

            // Verify the minted SSO bearer token is accepted by /mcp
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var mcpResp = await client.PostAsJsonAsync("/mcp", new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "ping"
            });
            mcpResp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Replaying the same SAML assertion must be rejected with generic error
            var replayResp = await client.PostAsJsonAsync("/api/v1/auth/enterprise/saml", new
            {
                samlAssertionXml = samlXml,
                tenantId = "tenant-icici"
            });
            replayResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var replayBody = await replayResp.Content.ReadAsStringAsync();
            replayBody.Should().Contain("Enterprise SSO authentication failed.");
            replayBody.Should().NotContain("redeemed");
        }

        [Fact]
        public async Task OidcExchange_WithForgedOrCrossTenantToken_DoesNotExposeExceptionMessages()
        {
            var client = _factory.CreateClient();
            using var attackerRsa = RSA.Create(2048);

            // 1. Forged key -> 401 with generic error
            var forgedToken = EnterpriseAuthenticationTests.CreateSignedOidcJwt(
                attackerRsa,
                issuer: OidcAuthority,
                audience: OidcAudience,
                claims: new[] { new Claim("sub", "attacker"), new Claim("tid", "tenant-hdfc") });

            var forgedResp = await client.PostAsJsonAsync("/api/v1/auth/enterprise/oidc", new
            {
                idToken = forgedToken,
                tenantId = "tenant-hdfc"
            });
            forgedResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var forgedBody = await forgedResp.Content.ReadAsStringAsync();
            forgedBody.Should().Contain("Enterprise SSO authentication failed.");
            forgedBody.Should().NotContain("IDX105");
            forgedBody.Should().NotContain("Signature");

            // 2. Cross-tenant -> 403 with generic error
            var crossTenantToken = EnterpriseAuthenticationTests.CreateSignedOidcJwt(
                _oidcRsa,
                issuer: OidcAuthority,
                audience: OidcAudience,
                claims: new[] { new Claim("sub", "attacker"), new Claim("tid", "tenant-rogue") });

            var crossResp = await client.PostAsJsonAsync("/api/v1/auth/enterprise/oidc", new
            {
                idToken = crossTenantToken,
                tenantId = "tenant-hdfc"
            });
            crossResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var crossBody = await crossResp.Content.ReadAsStringAsync();
            crossBody.Should().Contain("Access denied for the requested tenant.");
            crossBody.Should().NotContain("tenant-rogue");
        }

        [Fact]
        public async Task SsoEndpoints_EnforceRequestSizeLimits()
        {
            var client = _factory.CreateClient();

            var oversizedOidc = JsonSerializer.Serialize(new
            {
                idToken = new string('a', 80_000),
                tenantId = "tenant-hdfc"
            });
            var oidcResp = await client.PostAsync(
                "/api/v1/auth/enterprise/oidc",
                new StringContent(oversizedOidc, Encoding.UTF8, "application/json"));
            oidcResp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

            var oversizedSaml = JsonSerializer.Serialize(new
            {
                samlAssertionXml = new string('x', 600_000),
                tenantId = "tenant-icici"
            });
            var samlResp = await client.PostAsync(
                "/api/v1/auth/enterprise/saml",
                new StringContent(oversizedSaml, Encoding.UTF8, "application/json"));
            samlResp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        }

        [Fact]
        public async Task SsoEndpoints_EnforceRateLimiting()
        {
            using var isolatedFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("UseInMemoryStore", "true");
                builder.UseSetting("Jwt:SecretKey", JwtSecret);
            });
            var client = isolatedFactory.CreateClient();

            var statuses = new List<HttpStatusCode>();
            HttpResponseMessage? rateLimitedResponse = null;
            for (var i = 0; i < 15; i++)
            {
                var resp = await client.PostAsJsonAsync("/api/v1/auth/enterprise/oidc", new
                {
                    idToken = "invalid.token.value",
                    tenantId = "tenant-hdfc"
                });
                statuses.Add(resp.StatusCode);

                if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    rateLimitedResponse = resp;
                    break;
                }
            }

            rateLimitedResponse.Should().NotBeNull($"15 rapid requests should hit 429; observed statuses: {string.Join(", ", statuses)}");
            rateLimitedResponse!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }
    }
}
