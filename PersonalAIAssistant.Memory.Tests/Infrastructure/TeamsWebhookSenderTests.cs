using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.AI.Teams;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure
{
    public class TeamsWebhookSenderTests
    {
        private readonly Mock<IHttpClientFactory> _httpFactoryMock = new();
        private readonly Mock<IUrlSafetyValidator> _urlValidatorMock = new();

        [Fact]
        public async Task SendAsync_UnsafeWebhookUrl_ThrowsSsrfSecurityException()
        {
            var teamsOptions = Options.Create(new TeamsOptions
            {
                Enabled = true,
                WebhookUrl = "http://169.254.169.254/latest/meta-data/"
            });

            var threatOptions = Options.Create(new AiThreatOptions
            {
                EnableSsrfProtection = true,
                RequireHttps = true,
                AllowedWebhookHosts = ["outlook.office.com"]
            });

            _urlValidatorMock.Setup(v => v.ValidateUrl(
                "http://169.254.169.254/latest/meta-data/",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<bool>()))
                .Returns(new UrlSafetyResult(false, "Cloud metadata IP 169.254.169.254 is restricted."));

            var sender = new TeamsWebhookSender(
                _httpFactoryMock.Object,
                teamsOptions,
                _urlValidatorMock.Object,
                threatOptions,
                NullLogger<TeamsWebhookSender>.Instance);

            var act = () => sender.SendAsync("Alert", "Test body");

            await act.Should().ThrowAsync<SsrfSecurityException>()
                .WithMessage("*Cloud metadata IP 169.254.169.254 is restricted*");

            // Verify no HTTP client was created to execute the call
            _httpFactoryMock.Verify(h => h.CreateClient(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task SendAsync_DisabledTeams_DoesNotValidateOrSend()
        {
            var teamsOptions = Options.Create(new TeamsOptions
            {
                Enabled = false,
                WebhookUrl = "http://169.254.169.254/latest/meta-data/"
            });

            var threatOptions = Options.Create(new AiThreatOptions());

            var sender = new TeamsWebhookSender(
                _httpFactoryMock.Object,
                teamsOptions,
                _urlValidatorMock.Object,
                threatOptions,
                NullLogger<TeamsWebhookSender>.Instance);

            await sender.SendAsync("Alert", "Test body");

            _urlValidatorMock.Verify(v => v.ValidateUrl(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<bool>()), Times.Never);
        }
    }
}
