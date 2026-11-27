using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.AI.Teams;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security;

public sealed class TeamsWebhookSenderSecurityIntegrationTests
{
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("https://127.0.0.1/internal")]
    [InlineData("https://192.168.1.5/admin")]
    public async Task SendAsync_UnsafeDestination_IsBlockedByProductionValidatorBeforeNetwork(string url)
    {
        var handler = new RecordingHandler();
        var sender = new TeamsWebhookSender(
            new TestHttpClientFactory(handler),
            Options.Create(new TeamsOptions { Enabled = true, WebhookUrl = url }),
            new SsrfUrlValidator(
                Options.Create(new AiThreatOptions
                {
                    EnableSsrfProtection = true,
                    RequireHttps = true,
                    AllowedWebhookHosts = ["outlook.office.com"]
                }),
                NullLogger<SsrfUrlValidator>.Instance),
            Options.Create(new AiThreatOptions
            {
                EnableSsrfProtection = true,
                RequireHttps = true,
                AllowedWebhookHosts = ["outlook.office.com"]
            }),
            NullLogger<TeamsWebhookSender>.Instance);

        var act = () => sender.SendAsync("Security alert", "Sensitive test alert body");

        await act.Should().ThrowAsync<SsrfSecurityException>();
        handler.RequestCount.Should().Be(0, "the real sender must reject an unsafe destination before making an HTTP request");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public TestHttpClientFactory(HttpMessageHandler handler) => _client = new HttpClient(handler);

        public HttpClient CreateClient(string name) => _client;
    }
}
