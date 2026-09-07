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
        /// Retrieves the client/user-supplied AI provider credential (e.g. BYOK Gemini API key)
        /// provided dynamically when the user logs in to the AI application.
        /// </summary>
        string? GetApiKey(string providerName) => null;
    }
}
