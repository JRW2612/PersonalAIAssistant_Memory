using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class SsrfUrlValidatorTests
    {
        private readonly SsrfUrlValidator _validator;

        public SsrfUrlValidatorTests()
        {
            var options = Options.Create(new AiThreatOptions
            {
                EnableSsrfProtection = true,
                RequireHttps = true,
                AllowedWebhookHosts = ["outlook.office.com", "outlook.office365.com"]
            });

            _validator = new SsrfUrlValidator(options, NullLogger<SsrfUrlValidator>.Instance);
        }

        [Theory]
        [InlineData("http://169.254.169.254/latest/meta-data/")]
        [InlineData("https://169.254.169.254/latest/meta-data/")]
        [InlineData("http://169.254.169.254/computeMetadata/v1/")]
        public void ValidateUrl_AwsOrCloudMetadataIp_ShouldBeBlocked(string url)
        {
            var result = _validator.ValidateUrl(url, requireHttps: false);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("metadata");
        }

        [Theory]
        [InlineData("http://127.0.0.1/admin")]
        [InlineData("https://127.0.0.1:8080")]
        [InlineData("http://127.0.1.1")]
        [InlineData("http://localhost/secret")]
        [InlineData("https://localhost:5001")]
        [InlineData("http://app.localhost")]
        public void ValidateUrl_LoopbackAddress_ShouldBeBlocked(string url)
        {
            var result = _validator.ValidateUrl(url, requireHttps: false);

            result.IsSafe.Should().BeFalse();
        }

        [Theory]
        [InlineData("http://10.0.0.1/internal")]
        [InlineData("https://10.254.0.1/api")]
        [InlineData("http://172.16.0.1/")]
        [InlineData("http://172.31.255.255/")]
        [InlineData("http://192.168.1.1/router")]
        [InlineData("https://192.168.0.100/admin")]
        public void ValidateUrl_PrivateRfc1918Address_ShouldBeBlocked(string url)
        {
            var result = _validator.ValidateUrl(url, requireHttps: false);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("private");
        }

        [Theory]
        [InlineData("file:///etc/passwd")]
        [InlineData("ftp://internal.server/file.txt")]
        [InlineData("gopher://127.0.0.1:70/")]
        [InlineData("ldap://127.0.0.1:389/dc=example,dc=com")]
        public void ValidateUrl_DangerousScheme_ShouldBeBlocked(string url)
        {
            var result = _validator.ValidateUrl(url, requireHttps: false);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("Scheme");
        }

        [Fact]
        public void ValidateUrl_HttpWhenHttpsRequired_ShouldBeBlocked()
        {
            var result = _validator.ValidateUrl("http://outlook.office.com/webhook/123", requireHttps: true);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("HTTPS is required");
        }

        [Theory]
        [InlineData("http://2130706433")] // 127.0.0.1 in DWORD
        [InlineData("http://2852039166")] // 169.254.169.254 in DWORD
        [InlineData("http://0x7f000001")] // 127.0.0.1 in Hex
        public void ValidateUrl_EncodedAlternativeIpFormats_ShouldBeBlocked(string url)
        {
            var result = _validator.ValidateUrl(url, requireHttps: false);

            result.IsSafe.Should().BeFalse();
        }

        [Fact]
        public void ValidateUrl_HostNotInAllowlist_ShouldBeBlocked()
        {
            var allowedHosts = new[] { "outlook.office.com" };
            var result = _validator.ValidateUrl("https://evil.com/webhook", allowedHosts: allowedHosts, requireHttps: true);

            result.IsSafe.Should().BeFalse();
            result.Reason.Should().Contain("allowed hosts list");
        }

        [Theory]
        [InlineData("Check this out: http://169.254.169.254/latest/meta-data/iam/security-credentials/")]
        [InlineData("Internal status from http://127.0.0.1:8080/metrics")]
        [InlineData("Database dump at file:///etc/shadow")]
        [InlineData("Internal router: http://192.168.1.1/admin")]
        public void ScanText_TextWithSsrfTargets_ShouldBeDetected(string text)
        {
            var result = _validator.ScanText(text);

            result.HasUnsafeUrl.Should().BeTrue();
            result.MatchedUrl.Should().NotBeNullOrWhiteSpace();
        }

        [Theory]
        [InlineData("Today we discussed the quarterly project architecture.")]
        [InlineData("User reported an issue with login on the mobile device.")]
        [InlineData("Meeting notes from yesterday's retrospective.")]
        public void ScanText_CleanText_ShouldBeSafe(string text)
        {
            var result = _validator.ScanText(text);

            result.HasUnsafeUrl.Should().BeFalse();
            result.MatchedUrl.Should().BeNull();
        }
    }
}
