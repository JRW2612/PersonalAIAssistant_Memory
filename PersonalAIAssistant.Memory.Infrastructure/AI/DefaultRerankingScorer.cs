using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;

namespace PersonalAIAssistant.Memory.Infrastructure.AI
{
    /// <summary>
    /// Default scoring algorithm combining vector similarity, exponential recency decay, and importance weighting.
    /// Open for extension / replacement via DI.
    /// </summary>
    public class DefaultRerankingScorer : IRerankingScorer
    {
        private readonly double _vectorWeight;
        private readonly double _recencyWeight;
        private readonly double _importanceWeight;
        private readonly double _decayLambda;

        public DefaultRerankingScorer(
            double vectorWeight = 0.6,
            double recencyWeight = 0.3,
            double importanceWeight = 0.1,
            double decayLambda = 0.05)
        {
            _vectorWeight = vectorWeight;
            _recencyWeight = recencyWeight;
            _importanceWeight = importanceWeight;
            _decayLambda = decayLambda;
        }

        public double CalculateScore(double vectorScore, DateTime createdAt, MemoryImportance importance, out double recencyScore)
        {
            recencyScore = 1.0;
            if (createdAt != default)
            {
                var daysAgo = (DateTime.UtcNow - createdAt).TotalDays;
                if (daysAgo < 0) daysAgo = 0;
                recencyScore = Math.Exp(-_decayLambda * daysAgo);
            }

            double importanceScore = importance switch
            {
                MemoryImportance.High => 1.0,
                MemoryImportance.Medium => 0.5,
                MemoryImportance.Low => 0.1,
                _ => 0.5
            };

            return (_vectorWeight * vectorScore) + (_recencyWeight * recencyScore) + (_importanceWeight * importanceScore);
        }
    }
}
