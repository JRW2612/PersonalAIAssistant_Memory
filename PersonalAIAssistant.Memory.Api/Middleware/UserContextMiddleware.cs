using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Api.Middleware
{
    /// <summary>
    /// Derives user identity from the validated ClaimsPrincipal (JWT token) or authenticated context,
    /// preventing caller-supplied identity spoofing (SEC-02, SEC-06).
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
            string? userId = null;

            if (context.User.Identity?.IsAuthenticated == true)
            {
                userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) 
                      ?? context.User.FindFirstValue("sub")
                      ?? context.User.FindFirstValue(ClaimTypes.Name);
            }

            if (!string.IsNullOrWhiteSpace(userId))
            {
                context.Items["UserId"] = userId;
            }

            await _next(context);
        }
    }
}
