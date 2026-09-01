using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class RuleBasedDlpServiceTests
    {
        private static RuleBasedDlpService CreateService(bool enabled = true, bool block = true)
        {
            var opts = Options.Create(new DlpOptions { Enabled = enabled, BlockOnViolation = block });
            return new RuleBasedDlpService(opts, NullLogger<RuleBasedDlpService>.Instance);
        }

        [Fact]
        public void Scan_CleanText_ReturnsNoViolations()
        {
            var svc = CreateService();
            var result = svc.Scan("Today I discussed the quarterly roadmap with the team.");
            result.HasViolations.Should().BeFalse();
            result.Violations.Should().BeEmpty();
        }

        [Fact]
        public void Scan_SocialSecurityNumber_DetectsViolation()
        {
            var svc = CreateService();
            var result = svc.Scan("Employee SSN: 123-45-6789");
            result.HasViolations.Should().BeTrue();
            result.Violations.Should().ContainSingle(v => v.Category == DlpCategory.SocialSecurityNumber);
        }

        [Fact]
        public void Scan_PrivateKey_DetectsViolation()
        {
            var svc = CreateService();
            var result = svc.Scan("-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA...");
            result.HasViolations.Should().BeTrue();
            result.Violations.Should().ContainSingle(v => v.Category == DlpCategory.PrivateKey);
        }

        [Fact]
        public void Scan_OpenAiApiKey_DetectsViolation()
        {
            var svc = CreateService();
            var result = svc.Scan("My API key is sk-abcdefghijklmnopqrstuvwxyzABCDEFGH");
            result.HasViolations.Should().BeTrue();
            result.Violations.Should().ContainSingle(v => v.Category == DlpCategory.ApiKey);
        }

        [Fact]
        public void Scan_JwtToken_DetectsViolation()
        {
            var svc = CreateService();
            var result = svc.Scan("Token: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ1c2VyMTIzIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c");
            result.HasViolations.Should().BeTrue();
            result.Violations.Should().ContainSingle(v => v.Category == DlpCategory.JwtToken);
        }

        [Fact]
        public void Scan_WhenDisabled_ReturnsNoViolations()
        {
            var svc = CreateService(enabled: false);
            var result = svc.Scan("SSN: 123-45-6789");
            result.HasViolations.Should().BeFalse();
        }

        [Fact]
        public void Scan_EmptyText_ReturnsNoViolations()
        {
            var svc = CreateService();
            svc.Scan(string.Empty).HasViolations.Should().BeFalse();
            svc.Scan("   ").HasViolations.Should().BeFalse();
        }
    }
}
