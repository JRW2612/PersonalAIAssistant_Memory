namespace PersonalAIAssistant.Memory.Core.Interfaces.Security
{
    /// <summary>
    /// Contract for validating URLs against Server-Side Request Forgery (SSRF) threats.
    /// Defends against private network scanning, link-local / metadata exfiltration (e.g. AWS IMDS 169.254.169.254),
    /// loopback abuse, and unsafe schemes.
    /// </summary>
    public interface IUrlSafetyValidator
    {
        UrlSafetyResult ValidateUrl(string? url, IEnumerable<string>? allowedHosts = null, bool requireHttps = true);
        UrlSafetyScanResult ScanText(string? text);
    }

    public sealed record UrlSafetyResult(
        bool IsSafe,
        string Reason,
        string? Host = null,
        string? ResolvedIp = null);

    public sealed record UrlSafetyScanResult(
        bool HasUnsafeUrl,
        string? MatchedUrl,
        string Reason);
}
