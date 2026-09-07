using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using System.Security.Claims;

namespace PersonalAIAssistant.Memory.Api.Security;

/// <summary>
/// Resolves IUserContext from HttpContext claims principal.
/// SRP: only extracts identity from the HTTP request context.
/// </summary>
public sealed class HttpUserContext : IUserContext
{
    public string UserId { get; }
    public string TenantId { get; }
    public IReadOnlyList<string> Roles { get; }
    public bool IsAuthenticated { get; }

    private readonly IHttpContextAccessor _accessor;

    public HttpUserContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
        var user = accessor.HttpContext?.User;
        IsAuthenticated = user?.Identity?.IsAuthenticated == true;

        UserId = user?.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? user?.FindFirstValue("sub")
              ?? user?.FindFirstValue("client_id")
              ?? user?.FindFirstValue("workload_id")
              ?? user?.FindFirstValue(ClaimTypes.Name)
              ?? string.Empty;

        TenantId = user?.FindFirstValue("tid")
                ?? user?.FindFirstValue("tenant_id")
                ?? user?.FindFirstValue("tenantid")
                ?? "default";

        Roles = (user?.FindAll(ClaimTypes.Role)
                .Concat(user?.FindAll("roles") ?? Enumerable.Empty<Claim>())
                .Concat(user?.FindAll("role") ?? Enumerable.Empty<Claim>())
                .Select(c => c.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>()).AsReadOnly();
    }

    public string? GetApiKey(string providerName)
    {
        var context = _accessor.HttpContext;
        if (context == null) return null;

        // 1. Extract from client application request headers (e.g. X-Gemini-Api-Key, X-AI-Key)
        if (context.Request.Headers.TryGetValue($"X-{providerName}-Api-Key", out var providerKey) && !string.IsNullOrWhiteSpace(providerKey))
            return providerKey.ToString();

        if (context.Request.Headers.TryGetValue("X-AI-Key", out var generalKey) && !string.IsNullOrWhiteSpace(generalKey))
            return generalKey.ToString();

        if (context.Request.Headers.TryGetValue("X-AI-Provider-Key", out var altKey) && !string.IsNullOrWhiteSpace(altKey))
            return altKey.ToString();

        // 2. Extract from validated JWT token claims (injected when user logged in)
        var claimKey = context.User.FindFirstValue($"{providerName.ToLowerInvariant()}_api_key")
                    ?? context.User.FindFirstValue("ai_api_key");
        if (!string.IsNullOrWhiteSpace(claimKey))
            return claimKey;

        // 3. Extract from HttpContext.Items (session cache)
        if (context.Items.TryGetValue($"{providerName.ToLowerInvariant()}_api_key", out var itemKey) && itemKey is string s && !string.IsNullOrWhiteSpace(s))
            return s;

        return null;
    }
}
