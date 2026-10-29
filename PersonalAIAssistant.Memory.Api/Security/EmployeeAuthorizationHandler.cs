using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Api.Security;

/// <summary>
/// Fine-grained resource-based authorization handler for EmployeeRecord resources.
/// Enforces:
/// 1. Trusted Tenant Isolation: Caller's validated token tenant must match resource tenant.
/// 2. Ownership: Authenticated user identity from claims must match target employee UserId.
/// 3. Least Privilege for Administrators: Administrators are allowed access ONLY if explicit
///    permission claims (AppPermissions) are granted; blanket Role == Admin is rejected.
/// 4. Security Audit Trails: Logs all cross-tenant violations, policy denials, and granted operations.
/// </summary>
public class EmployeeAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, EmployeeRecord>
{
    private readonly ISecurityAuditLogger? _auditLogger;

    public EmployeeAuthorizationHandler(ISecurityAuditLogger? auditLogger = null)
    {
        _auditLogger = auditLogger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        EmployeeRecord resource)
    {
        var callerUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.User.FindFirstValue("sub")
                        ?? context.User.Identity?.Name;

        if (string.IsNullOrEmpty(callerUserId))
        {
            return Task.CompletedTask;
        }

        // 1. Trusted Tenant Isolation Check
        var callerTenantId = context.User.FindFirstValue("tid")
                          ?? context.User.FindFirstValue("tenant_id")
                          ?? context.User.FindFirstValue("tenantid")
                          ?? "default";

        if (!string.IsNullOrEmpty(resource.TenantId) &&
            !string.Equals(callerTenantId, "system", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(resource.TenantId, callerTenantId, StringComparison.OrdinalIgnoreCase))
        {
            // Security Audit Trail: Cross-tenant access attempt strictly logged and blocked
            _auditLogger?.LogCrossTenantAttempt(
                callerUserId,
                callerTenantId,
                resource.Id.ToString(),
                resource.TenantId,
                $"Employee:{requirement.Name}");

            return Task.CompletedTask;
        }

        // 2. Direct Ownership Check: Employees can read and update their own records
        var isOwner = string.Equals(resource.UserId, callerUserId, StringComparison.OrdinalIgnoreCase);
        if (isOwner)
        {
            if (requirement.Name == nameof(EmployeeOperations.Read) ||
                requirement.Name == nameof(EmployeeOperations.Update))
            {
                context.Succeed(requirement);
                _auditLogger?.LogAccessGranted(callerUserId, $"Employee:{requirement.Name}", resource.Id.ToString());
                return Task.CompletedTask;
            }
        }

        // 3. Explicit Permission Check for Administrators (Least Privilege)
        if (context.User.IsInRole("Admin") || context.User.IsInRole("Administrator"))
        {
            var permissions = context.User.FindAll(AppPermissions.ClaimType)
                .Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (requirement.Name == nameof(EmployeeOperations.Read) &&
                (permissions.Contains(AppPermissions.ReadAllEmployees) || permissions.Contains(AppPermissions.ManageEmployees)))
            {
                context.Succeed(requirement);
                _auditLogger?.LogAccessGranted(callerUserId, $"Employee:{requirement.Name}", resource.Id.ToString());
                return Task.CompletedTask;
            }

            if (requirement.Name == nameof(EmployeeOperations.Update) &&
                permissions.Contains(AppPermissions.ManageEmployees))
            {
                context.Succeed(requirement);
                _auditLogger?.LogAccessGranted(callerUserId, $"Employee:{requirement.Name}", resource.Id.ToString());
                return Task.CompletedTask;
            }

            if (requirement.Name == nameof(EmployeeOperations.Delete) &&
                (permissions.Contains(AppPermissions.DeleteEmployees) || permissions.Contains(AppPermissions.ManageEmployees)))
            {
                context.Succeed(requirement);
                _auditLogger?.LogAccessGranted(callerUserId, $"Employee:{requirement.Name}", resource.Id.ToString());
                return Task.CompletedTask;
            }
        }

        _auditLogger?.LogAccessDenied(callerUserId, $"Employee:{requirement.Name}", resource.Id.ToString(), "Authorization policy rejected operation.");
        return Task.CompletedTask;
    }
}
