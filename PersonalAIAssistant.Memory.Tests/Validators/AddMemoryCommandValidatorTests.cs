using FluentAssertions;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Validators;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using System.Collections.Generic;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Validators
{
    public class AddMemoryCommandValidatorTests
    {
        private readonly AddMemoryCommandValidator _validator = new();

        [Theory]
        [InlineData("Chat")]
        [InlineData("Email")]
        [InlineData("Note")]
        [InlineData("System")]
        [InlineData("User")]
        [InlineData("Other")]
        [InlineData("Api")]
        [InlineData("chat")] // Case insensitive check
        [InlineData("USER")]
        [InlineData("AuthenticationService")] // Custom service source string
        public void Validate_ValidMemorySource_ShouldNotHaveValidationError(string source)
        {
            var command = new AddMemoryCommand(
                RawText: "Valid memory text",
                Source: source,
                Importance: MemoryImportance.Medium,
                Tags: new List<string>(),
                UserId: "user-1",
                CorrelationId: null
            );

            var result = _validator.Validate(command);

            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Validate_EmptyMemorySource_ShouldHaveValidationError(string? source)
        {
            var command = new AddMemoryCommand(
                RawText: "Valid memory text",
                Source: source,
                Importance: MemoryImportance.Medium,
                Tags: new List<string>(),
                UserId: "user-1",
                CorrelationId: null
            );

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.PropertyName == "Source")
                .Which.ErrorMessage.Should().Contain("Source must not be empty");
        }

        [Fact]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "Testing null source edge case")]
        public void Validate_EmptyRawText_ShouldHaveValidationError()
        {
            var command = new AddMemoryCommand(
                RawText: "",
                Source: "User",
                Importance: MemoryImportance.Medium,
                Tags: new List<string>(),
                UserId: "user-1",
                CorrelationId: null
            );

            var result = _validator.Validate(command);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.PropertyName == "RawText");
        }
    }
}
