using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Services;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Handlers;

public class AutomaticMemoryFormationTests
{
    private const string UserTurn = "I prefer green tea.";

    [Fact]
    public async Task FormMemoriesFromUserTurnAsync_StoresExplicitFactWithEvidenceSource()
    {
        var fixture = new Fixture("""[{"subject":"user","predicate":"preferred_drink","object":"green tea","evidence":"I prefer green tea","isExplicit":true}]""");

        await fixture.Service.FormMemoriesFromUserTurnAsync("user-123", UserTurn);

        fixture.Mediator.Verify(m => m.Send(It.Is<AssertTemporalFactCommand>(command =>
            command.UserId == "user-123" && command.Subject == "user" &&
            command.Predicate == "preferred_drink" && command.Object == "green tea" &&
            command.Source == "AutomaticExtraction:ExplicitUserTurn" && command.Confidence == 0.9),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("I might prefer green tea.", "I might prefer green tea")]
    [InlineData("I do not prefer green tea.", "I do not prefer green tea")]
    [InlineData("I prefer green tea.", "My friend prefers green tea")]
    public async Task FormMemoriesFromUserTurnAsync_RejectsAmbiguousNegatedOrUnsupportedEvidence(string content, string evidence)
    {
        var fixture = new Fixture($"[{{\"subject\":\"user\",\"predicate\":\"preferred_drink\",\"object\":\"green tea\",\"evidence\":\"{evidence}\",\"isExplicit\":true}}]");

        await fixture.Service.FormMemoriesFromUserTurnAsync("user-123", content);

        fixture.Mediator.Verify(m => m.Send(It.IsAny<AssertTemporalFactCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FormMemoriesFromUserTurnAsync_DoesNotAutomaticallyReplaceConflictingFact()
    {
        var fixture = new Fixture("""[{"subject":"user","predicate":"preferred_drink","object":"green tea","evidence":"I prefer green tea","isExplicit":true}]""");
        fixture.Facts.Setup(repository => repository.GetCurrentFactsAsync("user-123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new TemporalFact(Guid.NewGuid(), "user", "preferred_drink", "coffee", DateTime.UtcNow) });

        await fixture.Service.FormMemoriesFromUserTurnAsync("user-123", UserTurn);

        fixture.Mediator.Verify(m => m.Send(It.IsAny<AssertTemporalFactCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FormMemoriesFromUserTurnAsync_SkipsSensitiveInputBeforeCallingProvider()
    {
        var fixture = new Fixture("[]");
        fixture.Sanitizer.Setup(s => s.Sanitize("Sensitive user content", null))
            .Returns(new SanitizedMemoryIngestion("[REDACTED:SENSITIVE]", Array.Empty<string>(), new[] { "sensitive" }));

        await fixture.Service.FormMemoriesFromUserTurnAsync("user-123", "Sensitive user content");

        fixture.Provider.Verify(p => p.GetResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Mediator.Verify(m => m.Send(It.IsAny<AssertTemporalFactCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Fixture
    {
        public Mock<IMediator> Mediator { get; } = new();
        public Mock<ITemporalFactRepository> Facts { get; } = new();
        public Mock<IMemoryIngestionSanitizer> Sanitizer { get; } = new();
        public Mock<IAIProvider> Provider { get; } = new();
        public AutomaticMemoryFormationService Service { get; }

        public Fixture(string response)
        {
            var providerFactory = new Mock<IAIProviderFactory>();
            providerFactory.Setup(factory => factory.GetProvider(null)).Returns(Provider.Object);
            Provider.Setup(provider => provider.GetResponseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(response);
            Sanitizer.Setup(sanitizer => sanitizer.Sanitize(It.IsAny<string>(), null))
                .Returns((string text, IReadOnlyCollection<string>? _) => new SanitizedMemoryIngestion(text, Array.Empty<string>(), Array.Empty<string>()));
            Facts.Setup(repository => repository.GetCurrentFactsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<TemporalFact>());
            Mediator.Setup(mediator => mediator.Send(It.IsAny<AssertTemporalFactCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Guid.NewGuid());

            Service = new AutomaticMemoryFormationService(
                providerFactory.Object,
                Mediator.Object,
                Facts.Object,
                Sanitizer.Object,
                Options.Create(new AutomaticMemoryOptions()),
                NullLogger<AutomaticMemoryFormationService>.Instance);
        }
    }
}
