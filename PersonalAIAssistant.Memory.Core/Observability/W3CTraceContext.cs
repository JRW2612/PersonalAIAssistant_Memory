using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PersonalAIAssistant.Memory.Core.Observability;

/// <summary>
/// Immutable value object representing a W3C Trace Context traceparent header
/// adhering to the W3C Trace Context recommendation (https://www.w3.org/TR/trace-context/).
/// Format: 00-{32hex_trace_id}-{16hex_span_id}-{2hex_flags} (55 characters).
/// </summary>
public sealed class W3CTraceContext : IEquatable<W3CTraceContext>
{
    private static readonly Regex TraceparentRegex = new(
        @"^00-([0-9a-f]{32})-([0-9a-f]{16})-([0-9a-f]{2})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public const string CurrentVersion = "00";
    public const string DefaultFlags = "01"; // Sampled
    public const string HeaderName = "traceparent";

    public string Version { get; }
    public string TraceId { get; }
    public string SpanId { get; }
    public string Flags { get; }

    public bool IsSampled => Flags.Length == 2 && (Convert.ToByte(Flags, 16) & 1) == 1;

    public W3CTraceContext(string traceId, string spanId, string flags = DefaultFlags, string version = CurrentVersion)
    {
        if (string.IsNullOrWhiteSpace(traceId) || traceId.Length != 32 || IsAllZeros(traceId))
            throw new ArgumentException("Trace ID must be a 32-character non-zero hexadecimal string.", nameof(traceId));

        if (string.IsNullOrWhiteSpace(spanId) || spanId.Length != 16 || IsAllZeros(spanId))
            throw new ArgumentException("Span ID must be a 16-character non-zero hexadecimal string.", nameof(spanId));

        if (string.IsNullOrWhiteSpace(flags) || flags.Length != 2 || !IsHex(flags))
            throw new ArgumentException("Flags must be a 2-character hexadecimal string.", nameof(flags));

        if (version != CurrentVersion)
            throw new ArgumentException($"Version '{version}' is not supported. Expected '{CurrentVersion}'.", nameof(version));

        Version = version.ToLowerInvariant();
        TraceId = traceId.ToLowerInvariant();
        SpanId = spanId.ToLowerInvariant();
        Flags = flags.ToLowerInvariant();
    }

    /// <summary>
    /// Generates a new W3C Trace Context with cryptographically secure random TraceId and SpanId.
    /// </summary>
    public static W3CTraceContext Create(bool sampled = true)
    {
        Span<byte> traceIdBytes = stackalloc byte[16];
        Span<byte> spanIdBytes = stackalloc byte[8];

        RandomNumberGenerator.Fill(traceIdBytes);
        RandomNumberGenerator.Fill(spanIdBytes);

        var traceId = Convert.ToHexString(traceIdBytes).ToLowerInvariant();
        var spanId = Convert.ToHexString(spanIdBytes).ToLowerInvariant();
        var flags = sampled ? "01" : "00";

        return new W3CTraceContext(traceId, spanId, flags);
    }

    /// <summary>
    /// Creates a child span with a new SpanId, retaining the original TraceId and Flags.
    /// </summary>
    public W3CTraceContext CreateChildSpan()
    {
        Span<byte> spanIdBytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(spanIdBytes);
        var childSpanId = Convert.ToHexString(spanIdBytes).ToLowerInvariant();

        return new W3CTraceContext(TraceId, childSpanId, Flags, Version);
    }

    /// <summary>
    /// Attempts to parse a W3C traceparent header string (00-{trace_id}-{span_id}-{flags}).
    /// Returns true if valid, false otherwise.
    /// </summary>
    public static bool TryParse(string? headerValue, out W3CTraceContext? context)
    {
        context = null;
        if (string.IsNullOrWhiteSpace(headerValue))
            return false;

        var trimmed = headerValue.Trim();
        if (trimmed.Length != 55)
            return false;

        var match = TraceparentRegex.Match(trimmed);
        if (!match.Success)
            return false;

        var traceId = match.Groups[1].Value.ToLowerInvariant();
        var spanId = match.Groups[2].Value.ToLowerInvariant();
        var flags = match.Groups[3].Value.ToLowerInvariant();

        if (IsAllZeros(traceId) || IsAllZeros(spanId))
            return false;

        context = new W3CTraceContext(traceId, spanId, flags, CurrentVersion);
        return true;
    }

    /// <summary>
    /// Parses a W3C traceparent header string, throwing FormatException if invalid.
    /// </summary>
    public static W3CTraceContext Parse(string headerValue)
    {
        if (TryParse(headerValue, out var context) && context != null)
            return context;

        throw new FormatException($"Invalid W3C traceparent format: '{headerValue}'. Expected format: '00-{{32hex}}-{{16hex}}-{{2hex}}'.");
    }

    /// <summary>
    /// Parses existing traceparent header if valid; otherwise generates a fresh W3C Trace Context.
    /// </summary>
    public static W3CTraceContext GetOrCreate(string? headerValue, bool sampled = true)
    {
        if (TryParse(headerValue, out var context) && context != null)
            return context;

        if (Activity.Current != null && Activity.Current.IdFormat == ActivityIdFormat.W3C)
        {
            var actTraceId = Activity.Current.TraceId.ToHexString();
            var actSpanId = Activity.Current.SpanId.ToHexString();
            var actFlags = Activity.Current.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ? "01" : "00";

            if (!IsAllZeros(actTraceId) && !IsAllZeros(actSpanId))
                return new W3CTraceContext(actTraceId, actSpanId, actFlags);
        }

        return Create(sampled);
    }

    /// <summary>
    /// Returns the standard W3C traceparent header representation.
    /// Example: 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01
    /// </summary>
    public override string ToString() => $"{Version}-{TraceId}-{SpanId}-{Flags}";

    public static implicit operator string(W3CTraceContext context) => context.ToString();

    public bool Equals(W3CTraceContext? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Version, other.Version, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(TraceId, other.TraceId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(SpanId, other.SpanId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Flags, other.Flags, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as W3CTraceContext);

    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Version),
            StringComparer.OrdinalIgnoreCase.GetHashCode(TraceId),
            StringComparer.OrdinalIgnoreCase.GetHashCode(SpanId),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Flags));

    private static bool IsAllZeros(string hex)
    {
        for (var i = 0; i < hex.Length; i++)
        {
            if (hex[i] != '0') return false;
        }
        return true;
    }

    private static bool IsHex(string str)
    {
        for (var i = 0; i < str.Length; i++)
        {
            var c = str[i];
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }
}
