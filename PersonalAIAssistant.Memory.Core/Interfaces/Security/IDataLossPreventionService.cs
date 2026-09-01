namespace PersonalAIAssistant.Memory.Core.Interfaces.Security
{
    /// <summary>Defines the Data Loss Prevention scanner contract (ISP: focused only on DLP scanning).</summary>
    public interface IDataLossPreventionService
    {
        /// <summary>Scans text for corporate policy violations (PII, credentials, regulated data).</summary>
        DlpScanResult Scan(string text);
    }

    public sealed record DlpScanResult(
        bool HasViolations,
        IReadOnlyList<DlpViolation> Violations);

    public sealed record DlpViolation(
        DlpCategory Category,
        string Description,
        int MatchPosition);

    public enum DlpCategory
    {
        SocialSecurityNumber,
        CreditCardNumber,
        ApiKey,
        PrivateKey,
        Password,
        JwtToken
    }
}
