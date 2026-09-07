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
    public class InputSanitizationBehaviorTests
    {
        private readonly Mock<ISecurityAuditLogger> _auditLoggerMock = new();

        private InputSanitizationBehavior<AddMemoryCommand, Guid> CreateBehavior(
            bool enabled = true,
            int maxChars = 1000)
        {
            var options = Options.Create(new AiThreatOptions
            {
                EnableInputSanitization = enabled,
                MaxInputLengthChars = maxChars
            });

            return new InputSanitizationBehavior<AddMemoryCommand, Guid>(
                options,
                _auditLoggerMock.Object,
                NullLogger<InputSanitizationBehavior<AddMemoryCommand, Guid>>.Instance);
        }

        [Fact]
        public async Task Handle_CleanInput_ProceedsToNext()
        {
            var behavior = CreateBehavior();
            var command = new AddMemoryCommand("Clean architectural context note.", "User", new List<string>(), "user-1");

            var nextCalled = false;
            RequestHandlerDelegate<Guid> next = (ct) =>
            {
                nextCalled = true;
                return Task.FromResult(Guid.NewGuid());
            };

            await behavior.Handle(command, next, CancellationToken.None);

            nextCalled.Should().BeTrue();
        }

        [Fact]
        public async Task Handle_NullByteInInput_ThrowsInputSanitizationException()
        {
            var behavior = CreateBehavior();
            var command = new AddMemoryCommand("Malicious\0string with null byte", "User", new List<string>(), "user-1");

            RequestHandlerDelegate<Guid> next = (ct) => Task.FromResult(Guid.NewGuid());

            Func<Task> act = () => behavior.Handle(command, next, CancellationToken.None);

            await act.Should().ThrowAsync<InputSanitizationException>()
                .Where(ex => ex.ViolationType == "NullByteDetected");

            _auditLoggerMock.Verify(a => a.LogSuspiciousInput("user-1", nameof(AddMemoryCommand), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Handle_UnicodeDirectionOverride_ThrowsInputSanitizationException()
        {
            var behavior = CreateBehavior();
            // \u202E is Right-to-Left Override character
            var command = new AddMemoryCommand("Disguised \u202E prompt injection attack", "User", new List<string>(), "user-1");

            RequestHandlerDelegate<Guid> next = (ct) => Task.FromResult(Guid.NewGuid());

            Func<Task> act = () => behavior.Handle(command, next, CancellationToken.None);

            await act.Should().ThrowAsync<InputSanitizationException>()
                .Where(ex => ex.ViolationType == "AdversarialUnicodeDetected");

            _auditLoggerMock.Verify(a => a.LogSuspiciousInput("user-1", nameof(AddMemoryCommand), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Handle_OversizedInput_ThrowsInputSanitizationException()
        {
            var behavior = CreateBehavior(maxChars: 50);
            var longText = new string('A', 100);
            var command = new AddMemoryCommand(longText, "User", new List<string>(), "user-1");

            RequestHandlerDelegate<Guid> next = (ct) => Task.FromResult(Guid.NewGuid());

            Func<Task> act = () => behavior.Handle(command, next, CancellationToken.None);

            await act.Should().ThrowAsync<InputSanitizationException>()
                .Where(ex => ex.ViolationType == "PayloadTooLarge");

            _auditLoggerMock.Verify(a => a.LogSuspiciousInput("user-1", nameof(AddMemoryCommand), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Handle_WhenDisabled_AllowsNullByteThrough()
        {
            var behavior = CreateBehavior(enabled: false);
            var command = new AddMemoryCommand("Malicious\0string", "User", new List<string>(), "user-1");

            var nextCalled = false;
            RequestHandlerDelegate<Guid> next = (ct) =>
            {
                nextCalled = true;
                return Task.FromResult(Guid.NewGuid());
            };

            await behavior.Handle(command, next, CancellationToken.None);

            nextCalled.Should().BeTrue();
        }
    }
}
