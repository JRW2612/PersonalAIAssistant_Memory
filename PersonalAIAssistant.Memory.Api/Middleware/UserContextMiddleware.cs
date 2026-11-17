using System.Security.Claims;

namespace PersonalAIAssistant.Memory.Api.Middleware;

/// <summary>
/// Derives user identity and tenant context from the validated ClaimsPrincipal (JWT token).
/// SRP: only populates HttpContext.Items — identity derivation delegated to HttpUserContext.
/// </summary>
public class UserContextMiddleware
{
    private readonly RequestDelegate _next;

    public UserContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.FindFirstValue("sub")
                      ?? context.User.FindFirstValue(ClaimTypes.Name);

            var tenantId = context.User.FindFirstValue("tid")
                        ?? context.User.FindFirstValue("tenant_id")
                        ?? context.User.FindFirstValue("tenantid")
                        ?? "default";

            if (!string.IsNullOrWhiteSpace(userId))
            {
                context.Items["UserId"] = userId;
                context.Items["TenantId"] = tenantId;
            }
        }

        var endpoint = context.GetEndpoint();
        var actionName = endpoint?.DisplayName ?? $"{context.Request.Method} {context.Request.Path}";
        context.Items["UserAction"] = actionName;

        await _next(context);
    }
}
