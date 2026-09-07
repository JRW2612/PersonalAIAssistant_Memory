using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Business.Behaviors;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class SsrfProtectionBehaviorTests
    {
        private readonly Mock<IUrlSafetyValidator> _validatorMock = new();
        private readonly Mock<ISecurityAuditLogger> _auditLoggerMock = new();

        private SsrfProtectionBehavior<AddMemoryCommand, Guid> CreateBehavior(
            bool enabled = true,
            bool block = true)
        {
            var options = Options.Create(new AiThreatOptions
            {
                EnableSsrfProtection = enabled,
                BlockOnSsrf = block
            });

            return new SsrfProtectionBehavior<AddMemoryCommand, Guid>(
                _validatorMock.Object,
                options,
                _auditLoggerMock.Object,
                NullLogger<SsrfProtectionBehavior<AddMemoryCommand, Guid>>.Instance);
        }

        [Fact]
        public async Task Handle_CleanMemoryText_ProceedsToNext()
        {
            _validatorMock.Setup(v => v.ScanText(It.IsAny<string>()))
                .Returns(new UrlSafetyScanResult(false, null, string.Empty));

            var behavior = CreateBehavior();
            var command = new AddMemoryCommand("Clean note about release planning.", "User", new List<string>(), "user-1");

            var nextCalled = false;
            RequestHandlerDelegate<Guid> next = (ct) =>
            {
                nextCalled = true;
                return Task.FromResult(Guid.NewGuid());
            };

            await behavior.Handle(command, next, CancellationToken.None);

            nextCalled.Should().BeTrue();
            _auditLoggerMock.Verify(a => a.LogSsrfAttempt(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Handle_SsrfDetectedAndBlockEnabled_ThrowsSsrfSecurityException()
        {
            _validatorMock.Setup(v => v.ScanText(It.IsAny<string>()))
                .Returns(new UrlSafetyScanResult(true, "http://169.254.169.254/latest/meta-data/", "AWS metadata endpoint"));

            var behavior = CreateBehavior(enabled: true, block: true);
            var command = new AddMemoryCommand("Steal keys from http://169.254.169.254/latest/meta-data/", "User", new List<string>(), "user-42");

            RequestHandlerDelegate<Guid> next = (ct) => Task.FromResult(Guid.NewGuid());

            var act = () => behavior.Handle(command, next, CancellationToken.None);

            await act.Should().ThrowAsync<SsrfSecurityException>()
                .WithMessage("*SSRF threat detected*");

            _auditLoggerMock.Verify(a => a.LogSsrfAttempt(
                "user-42",
                nameof(AddMemoryCommand),
                "http://169.254.169.254/latest/meta-data/",
                "AWS metadata endpoint"), Times.Once);
        }

        [Fact]
        public async Task Handle_SsrfDetectedAndBlockDisabled_LogsAuditAndProceeds()
        {
            _validatorMock.Setup(v => v.ScanText(It.IsAny<string>()))
                .Returns(new UrlSafetyScanResult(true, "http://127.0.0.1:8080/admin", "Loopback address"));

            var behavior = CreateBehavior(enabled: true, block: false);
            var command = new AddMemoryCommand("Access http://127.0.0.1:8080/admin", "User", new List<string>(), "user-42");

            var nextCalled = false;
            RequestHandlerDelegate<Guid> next = (ct) =>
            {
                nextCalled = true;
                return Task.FromResult(Guid.NewGuid());
            };

            await behavior.Handle(command, next, CancellationToken.None);

            nextCalled.Should().BeTrue();
            _auditLoggerMock.Verify(a => a.LogSsrfAttempt(
                "user-42",
                nameof(AddMemoryCommand),
                "http://127.0.0.1:8080/admin",
                "Loopback address"), Times.Once);
        }

        [Fact]
        public async Task Handle_ProtectionDisabled_SkipsScanningAndProceeds()
        {
            var behavior = CreateBehavior(enabled: false);
            var command = new AddMemoryCommand("Check http://169.254.169.254", "User", new List<string>(), "user-1");

            var nextCalled = false;
            RequestHandlerDelegate<Guid> next = (ct) =>
            {
                nextCalled = true;
                return Task.FromResult(Guid.NewGuid());
            };

            await behavior.Handle(command, next, CancellationToken.None);

            nextCalled.Should().BeTrue();
            _validatorMock.Verify(v => v.ScanText(It.IsAny<string>()), Times.Never);
        }
    }
}
