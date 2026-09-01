using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using System.Text.RegularExpressions;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    /// <summary>
    /// High-throughput regex + checksum DLP scanner. Follows OCP: new categories
    /// are added by extending the _rules list, not modifying existing logic.
    /// </summary>
    public sealed class RuleBasedDlpService : IDataLossPreventionService
    {
        private readonly DlpOptions _opts;
        private readonly ILogger<RuleBasedDlpService> _logger;

        private static readonly IReadOnlyList<DlpRule> Rules = new List<DlpRule>
        {
            new(DlpCategory.SocialSecurityNumber,
                new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled),
                "US Social Security Number detected"),

            new(DlpCategory.CreditCardNumber,
                new Regex(@"\b(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|3[47][0-9]{13}|6(?:011|5[0-9]{2})[0-9]{12})\b", RegexOptions.Compiled),
                "Credit card number detected",
                ValidateLuhn),

            new(DlpCategory.PrivateKey,
                new Regex(@"-----BEGIN\s+(?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                "Private cryptographic key detected"),

            new(DlpCategory.ApiKey,
                new Regex(@"\b(?:sk-[a-zA-Z0-9]{32,}|AKIA[0-9A-Z]{16}|ghp_[a-zA-Z0-9]{36}|glpat-[a-zA-Z0-9_-]{20}|AIzaSy[a-zA-Z0-9_-]{33})\b", RegexOptions.Compiled),
                "Cloud/AI API key detected"),

            new(DlpCategory.Password,
                new Regex(@"(?i)(?:password|passwd|pwd|secret)\s*[=:\s]\s*[\""']?[^\s\""']{8,}", RegexOptions.Compiled),
                "Plaintext password or credential assignment detected"),

            new(DlpCategory.JwtToken,
                new Regex(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}", RegexOptions.Compiled),
                "JWT token detected")
        };

        public RuleBasedDlpService(IOptions<DlpOptions> opts, ILogger<RuleBasedDlpService> logger)
        {
            _opts = opts.Value;
            _logger = logger;
        }

        public DlpScanResult Scan(string text)
        {
            if (!_opts.Enabled || string.IsNullOrWhiteSpace(text))
                return new DlpScanResult(false, Array.Empty<DlpViolation>());

            var violations = new List<DlpViolation>();

            foreach (var rule in Rules)
            {
                if (_opts.AllowedCategories.Contains(rule.Category.ToString(), StringComparer.OrdinalIgnoreCase))
                    continue;

                var match = rule.Pattern.Match(text);
                if (!match.Success) continue;

                if (rule.Validator != null && !rule.Validator(match.Value))
                    continue;

                violations.Add(new DlpViolation(rule.Category, rule.Description, match.Index));
                _logger.LogWarning("[DLP] {Category} violation detected at position {Position}: {Description}",
                    rule.Category, match.Index, rule.Description);
            }

            return new DlpScanResult(violations.Count > 0, violations.AsReadOnly());
        }

        private static bool ValidateLuhn(string number)
        {
            var digits = number.Where(char.IsDigit).Select(c => c - '0').ToArray();
            if (digits.Length < 13) return false;

            var sum = 0;
            var alternate = false;
            for (var i = digits.Length - 1; i >= 0; i--)
            {
                var d = digits[i];
                if (alternate) { d *= 2; if (d > 9) d -= 9; }
                sum += d;
                alternate = !alternate;
            }
            return sum % 10 == 0;
        }

        private sealed record DlpRule(
            DlpCategory Category,
            Regex Pattern,
            string Description,
            Func<string, bool>? Validator = null);
    }
}
