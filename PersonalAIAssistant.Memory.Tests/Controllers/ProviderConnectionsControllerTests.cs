using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalAIAssistant.Memory.Api.Controllers;
using PersonalAIAssistant.Memory.Api.DTOs;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Controllers
{
    public class ProviderConnectionsControllerTests
    {
        private readonly Mock<IProviderConnectionRepository> _repoMock;
        private readonly Mock<IProviderTokenService> _tokenServiceMock;
        private readonly Mock<IOAuthStateManager> _stateManagerMock;
        private readonly Mock<IOAuthProviderHandler> _handlerMock;
        private readonly Mock<IUserContext> _userContextMock;
        private readonly Mock<ISecurityAuditLogger> _auditLoggerMock;
        private readonly Mock<IAIProviderFactory> _aiFactoryMock;

        public ProviderConnectionsControllerTests()
        {
            _repoMock = new Mock<IProviderConnectionRepository>();
            _tokenServiceMock = new Mock<IProviderTokenService>();
            _stateManagerMock = new Mock<IOAuthStateManager>();
            _handlerMock = new Mock<IOAuthProviderHandler>();
            _userContextMock = new Mock<IUserContext>();
            _auditLoggerMock = new Mock<ISecurityAuditLogger>();
            _aiFactoryMock = new Mock<IAIProviderFactory>();

            _userContextMock.Setup(u => u.UserId).Returns("user-alice");
            _userContextMock.Setup(u => u.TenantId).Returns("default");
            _handlerMock.Setup(h => h.ProviderName).Returns("gemini");
        }

        private ProviderConnectionsController CreateController()
        {
            var controller = new ProviderConnectionsController(
                _repoMock.Object,
                _tokenServiceMock.Object,
                _stateManagerMock.Object,
                new[] { _handlerMock.Object },
                _userContextMock.Object,
                _auditLoggerMock.Object,
                _aiFactoryMock.Object,
                NullLogger<ProviderConnectionsController>.Instance
            );

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            return controller;
        }

        [Fact]
        public async Task GetConnections_Returns_AllSupportedProviders()
        {
            var controller = CreateController();
            _repoMock.Setup(r => r.GetUserConnectionsAsync("user-alice", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ProviderConnectionEntity>
                {
                    new()
                    {
                        UserId = "user-alice",
                        Provider = "gemini",
                        Status = ProviderConnectionStatus.Active,
                        ConsentScopes = "ai.generate"
                    }
                });

            var actionResult = await controller.GetConnections(CancellationToken.None);

            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            var dtos = Assert.IsAssignableFrom<IReadOnlyList<ProviderConnectionDto>>(okResult.Value);

            Assert.Equal(3, dtos.Count); // gemini, openai, anthropic

            var gemini = dtos.First(d => d.Provider == "gemini");
            Assert.True(gemini.IsActive);
            Assert.Equal("Active", gemini.Status);

            var openai = dtos.First(d => d.Provider == "openai");
            Assert.False(openai.IsActive);
            Assert.Equal("NotConnected", openai.Status);
        }

        [Fact]
        public void Authorize_Generates_AuthUrl_And_State()
        {
            var controller = CreateController();
            _stateManagerMock.Setup(s => s.Create("user-alice", "default", "gemini", It.IsAny<IEnumerable<string>>()))
                .Returns("signed.state.token");

            _handlerMock.Setup(h => h.BuildAuthorizationUrl(It.IsAny<string>(), "signed.state.token", It.IsAny<IEnumerable<string>>()))
                .Returns("https://accounts.google.com/o/oauth2/v2/auth?state=signed.state.token");

            var result = controller.Authorize("gemini", redirectUri: "https://localhost/callback", scopes: "ai.generate");

            var okResult = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<AuthorizeResponseDto>(okResult.Value);

            Assert.Equal("gemini", dto.Provider);
            Assert.Equal("signed.state.token", dto.State);
            Assert.Contains("accounts.google.com", dto.AuthorizationUrl);
        }

        [Fact]
        public async Task ConnectDirect_Encrypts_And_SavesConnection()
        {
            var controller = CreateController();
            _tokenServiceMock.Setup(t => t.EncryptToken("raw-api-key"))
                .Returns("enc-api-key");

            var request = new DirectConnectRequestDto(
                ApiKey: "raw-api-key",
                AccessToken: null,
                RefreshToken: null,
                ExpiresAtUtc: null,
                Scopes: new List<string> { "ai.generate", "ai.memory.read" },
                AccountEmail: "alice@example.com",
                AccountName: "Alice Personal Key"
            );

            var result = await controller.ConnectDirect("gemini", request, CancellationToken.None);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<ProviderConnectionDto>(okResult.Value);

            Assert.Equal("gemini", dto.Provider);
            Assert.True(dto.IsActive);
            _repoMock.Verify(r => r.UpsertConnectionAsync(It.Is<ProviderConnectionEntity>(c =>
                c.EncryptedAccessToken == "enc-api-key" &&
                c.AccountEmail == "alice@example.com"), It.IsAny<CancellationToken>()), Times.Once);
            _auditLoggerMock.Verify(a => a.LogProviderConnected("user-alice", "gemini", It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Revoke_Disperses_Revocation_And_Returns204()
        {
            var controller = CreateController();
            _repoMock.Setup(r => r.GetConnectionAsync("user-alice", "gemini", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProviderConnectionEntity
                {
                    UserId = "user-alice",
                    Provider = "gemini",
                    EncryptedAccessToken = "enc-token",
                    Status = ProviderConnectionStatus.Active
                });
            _tokenServiceMock.Setup(t => t.DecryptToken("enc-token")).Returns("plain-token");

            var result = await controller.Revoke("gemini", new RevokeConnectionRequestDto("User test revoke"), CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            _handlerMock.Verify(h => h.RevokeTokenAsync("plain-token", It.IsAny<CancellationToken>()), Times.Once);
            _repoMock.Verify(r => r.RevokeConnectionAsync("user-alice", "gemini", "User test revoke", It.IsAny<CancellationToken>()), Times.Once);
            _auditLoggerMock.Verify(a => a.LogProviderRevoked("user-alice", "gemini", "User test revoke"), Times.Once);
        }

        [Fact]
        public async Task GetAuditLogs_Returns_UserAuditTrail()
        {
            var controller = CreateController();
            _repoMock.Setup(r => r.GetAuditLogsAsync("user-alice", null, 50, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ProviderAuditLogEntity>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        UserId = "user-alice",
                        Provider = "gemini",
                        EventType = "CONNECTED",
                        Details = "OAuth Connected",
                        TimestampUtc = DateTime.UtcNow
                    }
                });

            var result = await controller.GetAuditLogs(null, 50, CancellationToken.None);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var dtos = Assert.IsAssignableFrom<IReadOnlyList<ProviderAuditLogDto>>(okResult.Value);

            Assert.Single(dtos);
            Assert.Equal("CONNECTED", dtos[0].EventType);
            Assert.Equal("gemini", dtos[0].Provider);
        }
    }
}
