using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    /// <summary>
    /// Detects prompt injection attacks, system prompt overrides, jailbreaks, and adversarial manipulation.
    /// Follows OCP: new attack patterns are added as rules in the static rule set without modifying scanning logic.
    /// </summary>
    public sealed class PatternBasedPromptInjectionDetector : IPromptInjectionDetector
    {
        private readonly AiThreatOptions _options;
        private readonly ILogger<PatternBasedPromptInjectionDetector> _logger;

        private sealed record InjectionRule(
            InjectionCategory Category,
            Regex Pattern,
            string Description);

        private static readonly IReadOnlyList<InjectionRule> Rules = new List<InjectionRule>
        {
            // 1. LLM Control / Context Token Injection (Highest priority: raw delimiters)
            new(
                InjectionCategory.ContextManipulation,
                new Regex(@"(?:\[SYSTEM\]|\[INST\]|<\|system\|>|<\|im_start\|>system|<\|user\|>|---\s*BEGIN\s+SYSTEM\s+PROMPT)",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase),
                "Raw LLM control token/delimiter injection"),

            // 2. System Prompt Overrides
            new(
                InjectionCategory.SystemPromptOverride,
                new Regex(@"\b(?:ignore|disregard|forget|override)\s+(?:all\s+)?(?:previous|prior|above|existing|system)?\s*(?:instructions|prompts|rules|guidelines|commands)\b",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase),
                "System prompt override attempt (e.g., 'ignore all previous instructions')"),

            new(
                InjectionCategory.SystemPromptOverride,
                new Regex(@"\bfrom\s+now\s+on\b.*?\b(?:ignore|disregard|do\s+not\s+follow|forget)\b",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase),
                "Conditional instruction override attempt"),

            // 3. Role Confusion & Impersonation
            new(
                InjectionCategory.RoleConfusion,
                new Regex(@"\byou\s+are\s+now\s+(?:an?\s+)?(?:unfiltered|unrestricted|evil|jailbroken|adversarial|developer|admin|root|system|superuser)\b",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase),
                "Role impersonation/confusion attempt (e.g., 'you are now an unrestricted developer')"),

            new(
                InjectionCategory.RoleConfusion,
                new Regex(@"\bpretend\s+(?:you\s+are|to\s+be)\s+(?:an?\s+)?(?:unfiltered|unrestricted|jailbroken|godmode|DAN|developer\s+mode)\b",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase),
                "Adversarial roleplay prompt injection"),

            new(
                InjectionCategory.RoleConfusion,
                new Regex(@"\bact\s+as\s+(?:an?\s+)?(?:unrestricted|DAN|jailbroken|evil\s+AI)\b",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase),
                "Persona hijacking attack"),

            // 4. Jailbreak Signatures
            new(
                InjectionCategory.JailbreakAttempt,
                new Regex(@"\b(?:DAN\s+mode|jailbreak|developer\s+mode\s+v\d+|god\s+mode|bypass\s+safety|disable\s+all\s+filters|anti-filter)\b",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase),
                "Known jailbreak signature (e.g., 'DAN mode', 'bypass safety')"),

            new(
                InjectionCategory.JailbreakAttempt,
                new Regex(@"\bdo\s+anything\s+now\b",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase),
                "Do Anything Now (DAN) attack variant"),

            // 5. Data / System Prompt Exfiltration
            new(
                InjectionCategory.DataExfiltration,
                new Regex(@"\b(?:print|reveal|output|display|show|dump|repeat)\s+(?:your\s+)?(?:entire\s+)?(?:system\s+prompt|initial\s+instructions|system\s+message|secret\s+key|prompt\s+template)\b",
                    RegexOptions.Compiled | RegexOptions.IgnoreCase),
                "System prompt exfiltration attempt")
        };

        public PatternBasedPromptInjectionDetector(
            IOptions<AiThreatOptions> options,
            ILogger<PatternBasedPromptInjectionDetector> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public PromptInjectionScanResult Scan(string text)
        {
            if (!_options.EnablePromptInjectionDetection || string.IsNullOrWhiteSpace(text))
            {
                return new PromptInjectionScanResult(false, null, InjectionCategory.None, string.Empty);
            }

            foreach (var rule in Rules)
            {
                var match = rule.Pattern.Match(text);
                if (match.Success)
                {
                    _logger.LogWarning(
                        "[THREAT DETECTED] Prompt injection detected: Category={Category} | Match='{Match}' | Rule='{Description}'",
                        rule.Category, match.Value, rule.Description);

                    return new PromptInjectionScanResult(
                        IsInjection: true,
                        MatchedPattern: match.Value,
                        Category: rule.Category,
                        Description: rule.Description);
                }
            }

            return new PromptInjectionScanResult(false, null, InjectionCategory.None, string.Empty);
        }
    }
}
