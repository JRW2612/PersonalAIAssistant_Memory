namespace PersonalAIAssistant.Memory.Core.Interfaces.AI
{
    /// <summary>
    /// Repository for persisting and querying structured agent execution traces.
    /// Traces capture the full lifecycle of a MediatR command/query decorated with
    /// the agent-tracing pipeline behavior, including pipeline timing and error categorization.
    /// </summary>
    public interface IAgentTraceRepository
    {
        /// <summary>Stores a completed agent trace.</summary>
        Task AddAsync(Models.AgentTrace trace, CancellationToken ct);

        /// <summary>
        /// Returns the most recent agent traces for the given user, newest first.
        /// </summary>
        Task<IReadOnlyList<Models.AgentTrace>> GetByUserAsync(
            string userId,
            int limit,
            CancellationToken ct);

        /// <summary>Returns a single trace by its unique ID, or null if not found.</summary>
        Task<Models.AgentTrace?> GetByIdAsync(Guid traceId, CancellationToken ct);

        /// <summary>
        /// Returns error traces for a tenant within a time window, optionally filtered by error category.
        /// </summary>
        Task<IReadOnlyList<Models.AgentTrace>> GetErrorsByTenantAsync(
            string tenantId,
            DateTimeOffset since,
            string? errorCategory,
            int limit,
            CancellationToken ct);
    }
}
