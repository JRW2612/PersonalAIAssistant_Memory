namespace PersonalAIAssistant.Memory.Core.Interfaces.Persistence;

/// <summary>
/// Generates structured EF Core query tags containing user action, identity, tenant, and tracing metadata
/// so that database engine logs (e.g. PostgreSQL, RDS, Slow Query Log) show which user action caused the query.
/// </summary>
public interface IDbQueryTagger
{
    /// <summary>
    /// Builds a formatted tag string for EF Core .TagWith().
    /// </summary>
    /// <param name="actionName">The specific user action or query purpose (e.g. "GetMemoriesByUser", "SearchMemories").</param>
    /// <param name="callerMember">Optional caller member name (auto-captured).</param>
    /// <param name="callerFile">Optional caller source file path (auto-captured).</param>
    /// <param name="callerLine">Optional caller source line number (auto-captured).</param>
    /// <returns>A standardized tag formatted for SQL query comments.</returns>
    string CreateTag(
        string actionName,
        string? callerMember = null,
        string? callerFile = null,
        int callerLine = 0);
}
