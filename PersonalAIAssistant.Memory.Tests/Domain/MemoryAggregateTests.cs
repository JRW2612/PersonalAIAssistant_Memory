using FluentAssertions;
using PersonalAIAssistant.Memory.Events;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Domains.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Domain
{
    public class MemoryAggregateTests
    {
        [Fact]
        public void AddMemory_Should_Generate_MemoryAddedEvent_And_Update_State()
        {
            // Arrange
            var aggregate = new MemoryAggregate();
            var rawText = "Testing domain aggregate event generation";
            var userId = "user-test-123";

            // Act
            aggregate.AddMemory(
                rawText: rawText,
                source: MemorySource.User,
                importance: MemoryImportance.High,
                tags: new List<string> { "unit-test" },
                userId: userId
            );

            // Assert
            aggregate.UncommittedEvents.Should().HaveCount(1);
            var evt = aggregate.UncommittedEvents.First().Should().BeOfType<MemoryAddedEvent>().Subject;
            evt.RawText.Should().Be(rawText);
            evt.UserId.Should().Be(userId);
            evt.Importance.Should().Be(MemoryImportance.High.ToString());
            aggregate.RawText.Should().Be(rawText);
        }

        [Fact]
        public void LoadFromHistory_Should_Rehydrate_Aggregate_State_Correctly()
        {
            // Arrange
            var aggregateId = Guid.NewGuid();
            var events = new List<MemoryEvent>
            {
                new MemoryAddedEvent
                {
                    AggregateId = aggregateId,
                    Version = 1,
                    RawText = "Initial memory content",
                    Source = MemorySource.User.ToString(),
                    Importance = MemoryImportance.Medium.ToString(),
                    UserId = "user-456",
                    Timestamp = DateTime.UtcNow.AddHours(-2)
                },
                new MemoryUpdatedEvent
                {
                    AggregateId = aggregateId,
                    Version = 2,
                    UserId = "user-456",
                    UpdatedFields = new Dictionary<string, string> { { "RawText", "Updated memory content" } },
                    Timestamp = DateTime.UtcNow.AddHours(-1)
                }
            };

            // Act
            var rehydrated = new MemoryAggregate(new MemoryId(aggregateId));
            rehydrated.LoadFromHistory(events);

            // Assert
            rehydrated.Version.Should().Be(2);
            rehydrated.RawText.Should().Be("Updated memory content");
            rehydrated.UncommittedEvents.Should().BeEmpty();
        }

        [Fact]
        public void Archive_Should_Mark_Memory_Archived_And_Emit_Event()
        {
            // Arrange
            var aggregate = new MemoryAggregate();
            aggregate.AddMemory("Memory to be archived", MemorySource.Chat, MemoryImportance.Low, new List<string>(), "user-789");
            aggregate.ClearUncommittedEvents();

            // Act
            aggregate.Archive("Testing archive reason", "user-789");

            // Assert
            aggregate.Status.Should().Be(MemoryStatus.Archived);
            aggregate.UncommittedEvents.Should().HaveCount(1);
            aggregate.UncommittedEvents.First().Should().BeOfType<MemoryArchivedEvent>();
        }
    }
}
