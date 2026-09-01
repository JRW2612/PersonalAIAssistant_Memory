using Microsoft.AspNetCore.Http;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using System.Security.Claims;

namespace PersonalAIAssistant.Memory.Api.Security
{
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

        public HttpUserContext(IHttpContextAccessor accessor)
        {
            var user = accessor.HttpContext?.User;
            IsAuthenticated = user?.Identity?.IsAuthenticated == true;

            UserId = user?.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user?.FindFirstValue("sub")
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
    }
}
