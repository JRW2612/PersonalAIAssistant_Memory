using FluentAssertions;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Events;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Domain;

public class TemporalFactTests
{
    [Fact]
    public void AssertTemporalFact_InitialAssertion_EmitsTemporalFactAssertedEvent()
    {
        var aggregate = new MemoryAggregate();

        aggregate.AssertTemporalFact(
            subject: "Alice",
            predicate: "works_at",
            @object: "Meta",
            confidence: 0.95,
            source: "InterviewTranscript",
            userId: "user-123");

        aggregate.UncommittedEvents.Should().HaveCount(1);
        var evt = aggregate.UncommittedEvents.First().Should().BeOfType<TemporalFactAssertedEvent>().Subject;
        evt.Subject.Should().Be("Alice");
        evt.Predicate.Should().Be("works_at");
        evt.Object.Should().Be("Meta");
        evt.Confidence.Should().Be(0.95);
        evt.Source.Should().Be("InterviewTranscript");

        aggregate.TemporalFacts.Should().HaveCount(1);
        var fact = aggregate.TemporalFacts.First();
        fact.IsCurrent.Should().BeTrue();
        fact.ValidToUtc.Should().BeNull();
        fact.Object.Should().Be("Meta");
    }

    [Fact]
    public void AssertTemporalFact_ContradictoryAssertion_InvalidatesPriorFact_AndAssertsNewFact()
    {
        var aggregate = new MemoryAggregate();

        // Step 1: Alice works at Meta
        aggregate.AssertTemporalFact("Alice", "works_at", "Meta", userId: "user-123");
        aggregate.ClearUncommittedEvents();

        // Step 2: Contradiction / Update -> Alice moved to Google
        aggregate.AssertTemporalFact("Alice", "works_at", "Google", userId: "user-123");

        aggregate.UncommittedEvents.Should().HaveCount(2);

        var invalidatedEvt = aggregate.UncommittedEvents[0].Should().BeOfType<TemporalFactInvalidatedEvent>().Subject;
        invalidatedEvt.Subject.Should().Be("Alice");
        invalidatedEvt.Predicate.Should().Be("works_at");
        invalidatedEvt.OldObject.Should().Be("Meta");
        invalidatedEvt.Reason.Should().Contain("Superseded");

        var assertedEvt = aggregate.UncommittedEvents[1].Should().BeOfType<TemporalFactAssertedEvent>().Subject;
        assertedEvt.Subject.Should().Be("Alice");
        assertedEvt.Predicate.Should().Be("works_at");
        assertedEvt.Object.Should().Be("Google");

        // Verify aggregate state
        aggregate.TemporalFacts.Should().HaveCount(2);

        var oldFact = aggregate.TemporalFacts.First(f => f.Object == "Meta");
        oldFact.IsCurrent.Should().BeFalse();
        oldFact.ValidToUtc.Should().NotBeNull();

        var newFact = aggregate.TemporalFacts.First(f => f.Object == "Google");
        newFact.IsCurrent.Should().BeTrue();
        newFact.ValidToUtc.Should().BeNull();
    }

    [Fact]
    public void AssertTemporalFact_SameObject_IsIdempotent()
    {
        var aggregate = new MemoryAggregate();

        aggregate.AssertTemporalFact("Alice", "preferred_editor", "VSCode", userId: "user-123");
        aggregate.ClearUncommittedEvents();

        // Re-assert identical fact
        aggregate.AssertTemporalFact("Alice", "preferred_editor", "VSCode", userId: "user-123");

        aggregate.UncommittedEvents.Should().BeEmpty();
        aggregate.TemporalFacts.Should().HaveCount(1);
    }

    [Fact]
    public void AssertTemporalFact_OrthogonalFacts_DoNotInvalidateEachOther()
    {
        var aggregate = new MemoryAggregate();

        aggregate.AssertTemporalFact("Alice", "works_at", "Google", userId: "user-123");
        aggregate.AssertTemporalFact("Alice", "lives_in", "Seattle", userId: "user-123");

        aggregate.TemporalFacts.Should().HaveCount(2);
        aggregate.TemporalFacts.Should().OnlyContain(f => f.IsCurrent);
    }
}
