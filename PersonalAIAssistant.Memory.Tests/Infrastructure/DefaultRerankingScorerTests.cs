using FluentAssertions;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Infrastructure.AI;
using System;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Infrastructure
{
    public class DefaultRerankingScorerTests
    {
        private readonly DefaultRerankingScorer _scorer = new();

        [Fact]
        public void CalculateScore_RecentHighImportance_YieldsHighScore()
        {
            var createdAt = DateTime.UtcNow;
            var vectorScore = 0.9;
            
            var score = _scorer.CalculateScore(vectorScore, createdAt, MemoryImportance.High, out var recencyScore);

            recencyScore.Should().BeApproximately(1.0, 0.05);
            // (0.6 * 0.9) + (0.3 * 1.0) + (0.1 * 1.0) = 0.54 + 0.30 + 0.10 = 0.94
            score.Should().BeApproximately(0.94, 0.05);
        }

        [Fact]
        public void CalculateScore_OldLowImportance_YieldsLowerScore()
        {
            var createdAt = DateTime.UtcNow.AddDays(-100);
            var vectorScore = 0.5;

            var score = _scorer.CalculateScore(vectorScore, createdAt, MemoryImportance.Low, out var recencyScore);

            recencyScore.Should().BeLessThan(0.1);
            score.Should().BeLessThan(0.4);
        }
    }
}
