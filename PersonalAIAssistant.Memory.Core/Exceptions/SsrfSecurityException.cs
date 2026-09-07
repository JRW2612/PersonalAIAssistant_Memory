namespace PersonalAIAssistant.Memory.Core.Exceptions
{
    /// <summary>
    /// Thrown when an outbound request or user input triggers an SSRF (Server-Side Request Forgery) protection rule.
    /// </summary>
    public sealed class SsrfSecurityException : Exception
    {
        public string? TargetUrl { get; }
        public string Reason { get; }

        public SsrfSecurityException(string? targetUrl, string reason)
            : base($"SSRF Protection: The URL '{targetUrl}' was blocked. Reason: {reason}")
        {
            TargetUrl = targetUrl;
            Reason = reason;
        }
    }
}
