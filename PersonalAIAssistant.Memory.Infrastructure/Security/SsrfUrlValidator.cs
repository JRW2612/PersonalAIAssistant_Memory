using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    /// <summary>
    /// Validates URLs and memory text against Server-Side Request Forgery (SSRF) threats.
    /// Defends against:
    /// 1. Cloud metadata endpoints (AWS IMDS / Azure / GCP: 169.254.169.254, metadata.google.internal)
    /// 2. Loopback addresses (127.0.0.0/8, ::1, localhost)
    /// 3. Private RFC 1918 networks (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16)
    /// 4. Link-local and multicast ranges
    /// 5. Alternative IP encodings (dword, hex, IPv4-mapped IPv6)
    /// 6. DNS rebinding / internal domain names (.local, .internal, .lan)
    /// 7. Dangerous URL schemes (file://, gopher://, ftp://, ldap://)
    /// </summary>
    public sealed class SsrfUrlValidator : IUrlSafetyValidator
    {
        private readonly AiThreatOptions _options;
        private readonly ILogger<SsrfUrlValidator> _logger;

        private static readonly Regex EmbeddedUrlRegex = new(
            @"(https?://[^\s""'<>]+|ftp://[^\s""'<>]+|file://[^\s""'<>]+|gopher://[^\s""'<>]+|ldap://[^\s""'<>]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex InternalHostRegex = new(
            @"\b(?:169\.254\.169\.254|127\.0\.0\.1|0\.0\.0\.0|localhost|metadata\.google\.internal|instance-data)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public SsrfUrlValidator(
            IOptions<AiThreatOptions> options,
            ILogger<SsrfUrlValidator> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public UrlSafetyResult ValidateUrl(string? url, IEnumerable<string>? allowedHosts = null, bool requireHttps = true)
        {
            if (!_options.EnableSsrfProtection)
            {
                return new UrlSafetyResult(true, "SSRF protection is disabled.");
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                return new UrlSafetyResult(false, "URL is null or empty.");
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return new UrlSafetyResult(false, "Invalid URL format.");
            }

            // Scheme validation
            var isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            var isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

            if (!isHttps && !isHttp)
            {
                return new UrlSafetyResult(false, $"Scheme '{uri.Scheme}' is not permitted. Only HTTP/HTTPS are allowed.");
            }

            if (requireHttps && !isHttps)
            {
                return new UrlSafetyResult(false, $"HTTPS is required but found scheme '{uri.Scheme}'.");
            }

            var host = uri.Host;
            if (string.IsNullOrWhiteSpace(host))
            {
                return new UrlSafetyResult(false, "URL does not contain a valid host.");
            }

            // Check against allowed hosts if specified
            var hostList = allowedHosts?.ToList();
            if (hostList != null && hostList.Count > 0)
            {
                var isAllowed = hostList.Any(allowed =>
                    string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase));

                if (!isAllowed)
                {
                    _logger.LogWarning("[SSRF] Host '{Host}' is not in allowed hosts list: [{AllowedHosts}].",
                        host, string.Join(", ", hostList));
                    return new UrlSafetyResult(false, $"Host '{host}' is not in the allowed hosts list.", host);
                }
            }

            // Check known internal/metadata domain names
            if (IsKnownInternalHost(host))
            {
                _logger.LogWarning("[SSRF] Blocked known internal or metadata host: '{Host}'.", host);
                return new UrlSafetyResult(false, $"Host '{host}' is a known internal or metadata endpoint.", host);
            }

            // Alternative encodings check: numeric DWORD / Hex
            if (TryEvaluateEncodedIp(host, out var encodedIpResult))
            {
                return encodedIpResult;
            }

            // If host is an IP literal
            if (IPAddress.TryParse(host, out var ipAddress))
            {
                if (IsBlockedIp(ipAddress))
                {
                    _logger.LogWarning("[SSRF] Blocked IP literal address: '{Ip}'.", ipAddress);
                    return new UrlSafetyResult(false, $"IP address '{ipAddress}' belongs to a private, loopback, or metadata subnet.", host, ipAddress.ToString());
                }

                return new UrlSafetyResult(true, "URL is safe.", host, ipAddress.ToString());
            }

            // Host is a domain name — perform DNS resolution to prevent DNS rebinding
            try
            {
                var addresses = Dns.GetHostAddresses(host);
                if (addresses.Length == 0)
                {
                    return new UrlSafetyResult(false, $"Host '{host}' could not be resolved to any IP address.", host);
                }

                foreach (var address in addresses)
                {
                    if (IsBlockedIp(address))
                    {
                        _logger.LogWarning("[SSRF] Host '{Host}' resolved to blocked IP '{Ip}'.", host, address);
                        return new UrlSafetyResult(false, $"Host '{host}' resolved to restricted IP address '{address}'.", host, address.ToString());
                    }
                }

                return new UrlSafetyResult(true, "URL is safe.", host, addresses[0].ToString());
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "[SSRF] DNS resolution failed for host '{Host}'.", host);
                return new UrlSafetyResult(false, $"DNS resolution failed for host '{host}': {ex.Message}", host);
            }
        }

        public UrlSafetyScanResult ScanText(string? text)
        {
            if (!_options.EnableSsrfProtection || string.IsNullOrWhiteSpace(text))
            {
                return new UrlSafetyScanResult(false, null, string.Empty);
            }

            // Quick check for high-risk targets
            var internalMatch = InternalHostRegex.Match(text);
            if (internalMatch.Success)
            {
                return new UrlSafetyScanResult(true, internalMatch.Value, $"Text references forbidden internal or metadata endpoint: '{internalMatch.Value}'.");
            }

            // Scan extracted URLs
            var urlMatches = EmbeddedUrlRegex.Matches(text);
            foreach (Match match in urlMatches)
            {
                var candidate = match.Value;
                var result = ValidateUrl(candidate, requireHttps: false);
                if (!result.IsSafe)
                {
                    return new UrlSafetyScanResult(true, candidate, result.Reason);
                }
            }

            return new UrlSafetyScanResult(false, null, string.Empty);
        }

        private static bool IsKnownInternalHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".corp", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "instance-data", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool TryEvaluateEncodedIp(string host, out UrlSafetyResult result)
        {
            result = new UrlSafetyResult(true, string.Empty);

            // Dword numeric IP e.g. 2130706433 (127.0.0.1) or 2852039166 (169.254.169.254)
            if (long.TryParse(host, out var dword) && dword >= 0 && dword <= uint.MaxValue)
            {
                var uintVal = (uint)dword;
                var ip = new IPAddress(new byte[]
                {
                    (byte)((uintVal >> 24) & 0xFF),
                    (byte)((uintVal >> 16) & 0xFF),
                    (byte)((uintVal >> 8) & 0xFF),
                    (byte)(uintVal & 0xFF)
                });

                if (IsBlockedIp(ip))
                {
                    result = new UrlSafetyResult(false, $"Host '{host}' is an encoded integer of blocked IP {ip}.", host, ip.ToString());
                    return true;
                }
            }

            // Hex numeric IP e.g. 0x7f000001
            if (host.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(host[2..], System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
            {
                var ip = new IPAddress(new byte[]
                {
                    (byte)((hexVal >> 24) & 0xFF),
                    (byte)((hexVal >> 16) & 0xFF),
                    (byte)((hexVal >> 8) & 0xFF),
                    (byte)(hexVal & 0xFF)
                });

                if (IsBlockedIp(ip))
                {
                    result = new UrlSafetyResult(false, $"Host '{host}' is a hex encoding of blocked IP {ip}.", host, ip.ToString());
                    return true;
                }
            }

            return false;
        }

        public static bool IsBlockedIp(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.Broadcast))
            {
                return true;
            }

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();

                // 0.0.0.0/8 (Current network)
                if (bytes[0] == 0) return true;

                // 10.0.0.0/8 (Private RFC 1918)
                if (bytes[0] == 10) return true;

                // 127.0.0.0/8 (Loopback)
                if (bytes[0] == 127) return true;

                // 100.64.0.0/10 (Carrier-Grade NAT)
                if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;

                // 169.254.0.0/16 (Link-Local & Cloud Metadata e.g. AWS IMDS 169.254.169.254)
                if (bytes[0] == 169 && bytes[1] == 254) return true;

                // 172.16.0.0/12 (Private RFC 1918)
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;

                // 192.0.0.0/24 (IETF Protocol Assignments)
                if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) return true;

                // 192.0.2.0/24 (TEST-NET-1)
                if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) return true;

                // 192.168.0.0/16 (Private RFC 1918)
                if (bytes[0] == 192 && bytes[1] == 168) return true;

                // 198.18.0.0/15 (Network benchmark tests)
                if (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19)) return true;

                // 198.51.100.0/24 (TEST-NET-2)
                if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) return true;

                // 203.0.113.0/24 (TEST-NET-3)
                if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) return true;

                // 224.0.0.0/4 (Multicast)
                if (bytes[0] >= 224 && bytes[0] <= 239) return true;

                // 240.0.0.0/4 (Reserved / Future use)
                if (bytes[0] >= 240) return true;

                return false;
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
                {
                    return true;
                }

                var bytes = ip.GetAddressBytes();

                // Unique Local Address (fc00::/7)
                if ((bytes[0] & 0xfe) == 0xfc) return true;

                // IPv6 loopback (::1) or unspecified (::)
                if (ip.Equals(IPAddress.IPv6Loopback) || ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Any))
                    return true;

                // IPv6 link-local (fe80::/10)
                if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true;

                // Discard prefix (100::/64)
                if (bytes[0] == 0x01 && bytes[1] == 0x00) return true;

                return false;
            }

            return false;
        }
    }
}
