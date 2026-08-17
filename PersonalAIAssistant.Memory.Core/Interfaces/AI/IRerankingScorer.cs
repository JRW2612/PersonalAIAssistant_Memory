using PersonalAIAssistant.Memory.Core.Domains.Enums;

namespace PersonalAIAssistant.Memory.Core.Interfaces.AI
{
    /// <summary>
    /// Encapsulates memory relevance scoring strategy combining vector similarity, recency decay, and importance weighting.
    /// Open for extension with custom reranking or machine-learned scoring models.
    /// </summary>
    public interface IRerankingScorer
    {
        double CalculateScore(double vectorScore, DateTime createdAt, MemoryImportance importance, out double recencyScore);
    }
}
