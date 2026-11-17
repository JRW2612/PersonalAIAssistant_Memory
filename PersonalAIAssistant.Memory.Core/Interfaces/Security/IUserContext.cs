namespace PersonalAIAssistant.Memory.Core.Interfaces.Security
{
    /// <summary>
    /// Strongly-typed user context derived from validated JWT claims.
    /// ISP: focused solely on identity resolution, not authentication logic.
    /// </summary>
    public interface IUserContext
    {
        string UserId { get; }
        string TenantId { get; }
        IReadOnlyList<string> Roles { get; }
        bool IsAuthenticated { get; }
        bool IsAdmin => Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase);
        bool IsAuditor => Roles.Contains("ComplianceAuditor", StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Optional preferred AI provider set by the user (e.g. "gemini", "openai").
        /// </summary>
        string? PreferredProvider => null;

        /// <summary>
        /// Retrieves the client/user-supplied AI provider credential (e.g. BYOK Gemini API key)
        /// provided dynamically when the user logs in to the AI application.
        /// </summary>
        string? GetApiKey(string providerName) => null;

        /// <summary>
        /// The current user action or operation initiating the request (e.g. "GetMemoriesByUser", "HTTP GET /api/memories").
        /// </summary>
        string? CurrentAction => null;

        /// <summary>
        /// Current distributed trace or correlation identifier for tracking queries back to incoming requests.
        /// </summary>
        string? CorrelationId => null;

        /// <summary>
        /// W3C traceparent header value (00-{trace_id}-{span_id}-{flags}) for distributed tracing.
        /// </summary>
        string? Traceparent => null;
    }
}
