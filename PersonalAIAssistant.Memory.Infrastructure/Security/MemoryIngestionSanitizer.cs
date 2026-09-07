using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.Security;

/// <summary>
/// Produces the only representation eligible for event and vector persistence. Raw input is never
/// retained alongside its redacted counterpart.
/// </summary>
public sealed class MemoryIngestionSanitizer : IMemoryIngestionSanitizer
{
    private static readonly (string Category, Regex Pattern)[] SensitivePatterns =
    {
        ("credential", new Regex(@"\b(?:sk-[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9]{36}|AIzaSy[A-Za-z0-9_-]{20,})\b", RegexOptions.Compiled)),
        ("private-key", new Regex(@"-----BEGIN\s+(?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----[\s\S]*?-----END\s+(?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("payment-card", new Regex(@"\b(?:4\d{12}(?:\d{3})?|5[1-5]\d{14}|3[47]\d{13}|6(?:011|5\d{2})\d{12})\b", RegexOptions.Compiled)),
        ("ssn", new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)),
        ("jwt", new Regex(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b", RegexOptions.Compiled)),
        ("password", new Regex(@"(?i)(?:password|passwd|pwd|secret)\s*[=:]\s*[^\s\""']+", RegexOptions.Compiled))
    };

    private readonly MemoryIngestionOptions _options;

    public MemoryIngestionSanitizer(IOptions<MemoryIngestionOptions> options) => _options = options.Value;

    public SanitizedMemoryIngestion Sanitize(string text, IReadOnlyCollection<string>? tags)
    {
        var sanitized = Normalize(text ?? string.Empty);
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_options.RedactSensitiveData)
        {
            foreach (var (category, pattern) in SensitivePatterns)
            {
                if (!pattern.IsMatch(sanitized)) continue;
                categories.Add(category);
                sanitized = pattern.Replace(sanitized, $"[REDACTED:{category.ToUpperInvariant()}]");
            }
        }

        var safeTags = (tags ?? Array.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => Normalize(tag).Trim())
            .Where(tag => tag.Length > 0 && tag.Length <= _options.MaxTagLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(_options.MaxTags)
            .ToList();

        // Classification labels take precedence so a caller cannot crowd them out with arbitrary tags.
        var categorizedTags = categories.Select(category => $"sensitivity:{category}")
            .Concat(safeTags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(_options.MaxTags)
            .ToList();
        return new SanitizedMemoryIngestion(sanitized, categorizedTags, categories.ToList());
    }

    private static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC);
        var result = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (ch == '\0' || (char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t')) continue;
            result.Append(ch);
        }
        return result.ToString();
    }
}
