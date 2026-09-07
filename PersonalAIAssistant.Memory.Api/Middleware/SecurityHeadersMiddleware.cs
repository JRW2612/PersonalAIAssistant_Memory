using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Api.Middleware;

/// <summary>
/// Injects enterprise HTTP security response headers to defend against clickjacking,
/// MIME-type confusion, XSS, and reconnaissance fingerprinting by autonomous exploit agents.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Prevent MIME-sniffing
            headers["X-Content-Type-Options"] = "nosniff";

            // Prevent clickjacking via iframes
            headers["X-Frame-Options"] = "DENY";

            // Legacy cross-site scripting filter
            headers["X-XSS-Protection"] = "1; mode=block";

            // Restrict referrer leakage
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Content Security Policy
            headers["Content-Security-Policy"] = "default-src 'self'";

            // Restrict browser hardware APIs
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

            // Remove fingerprinting headers
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
