using System.Security.Claims;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models.Security;

namespace PersonalAIAssistant.Memory.Infrastructure.Security
{
    public sealed class UiCapabilitiesService : IUiCapabilitiesService
    {
        public UiCapabilitiesDescriptor BuildCapabilities(ClaimsPrincipal principal)
        {
            if (principal == null || !principal.Identity?.IsAuthenticated == true)
            {
                return CreateAnonymousCapabilities();
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? principal.FindFirst("sub")?.Value
                      ?? principal.FindFirst("oid")?.Value
                      ?? principal.Identity?.Name
                      ?? "anonymous";

            var tenantId = principal.FindFirst("tid")?.Value
                        ?? principal.FindFirst("tenant_id")?.Value
                        ?? principal.FindFirst("tenantid")?.Value
                        ?? "default";

            var roles = principal.FindAll(ClaimTypes.Role)
                .Concat(principal.FindAll("role"))
                .Select(c => c.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var permissions = principal.FindAll("permission")
                .Concat(principal.FindAll("permissions"))
                .Select(c => c.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var isSuperAdmin = roles.Contains("SuperAdmin", StringComparer.OrdinalIgnoreCase) ||
                               string.Equals(tenantId, "system", StringComparison.OrdinalIgnoreCase);

            var isTenantAdmin = isSuperAdmin ||
                                roles.Contains("Admin", StringComparer.OrdinalIgnoreCase) ||
                                roles.Contains("TenantAdmin", StringComparer.OrdinalIgnoreCase) ||
                                roles.Contains("Administrator", StringComparer.OrdinalIgnoreCase);

            var isAuditor = roles.Contains("Auditor", StringComparer.OrdinalIgnoreCase) ||
                            roles.Contains("SecurityOfficer", StringComparer.OrdinalIgnoreCase);

            // Capabilities resolution
            var canCreateMemory = true; // Any authenticated user can create memories
            var canDeleteMemory = isTenantAdmin || permissions.Contains(UiPermissions.MemoryDelete);
            var canPurgeMemories = isSuperAdmin || permissions.Contains(UiPermissions.MemoryPurge);

            var canManageConnections = isTenantAdmin || permissions.Contains(UiPermissions.ProviderConnect);
            var canRevokeConnections = isTenantAdmin || permissions.Contains(UiPermissions.ProviderRevoke);
            var canTestConnections = true;

            var canViewHsmStatus = isTenantAdmin || isAuditor || permissions.Contains(UiPermissions.HsmRead);
            var canRotateHsmKeys = isSuperAdmin || permissions.Contains(UiPermissions.HsmRotate);

            var canViewAuditLogs = isTenantAdmin || isAuditor || permissions.Contains(UiPermissions.AuditRead);
            var canManageEmployees = isTenantAdmin || permissions.Contains(UiPermissions.EmployeeManage);

            // Construct visible navigation
            var navItems = new List<NavigationItem>
            {
                new("memories", "Memories", "/memories", "brain", UiPermissions.MemoryRead),
                new("connections", "AI Connections", "/connections", "link", UiPermissions.ProviderTest)
            };

            if (canManageEmployees)
            {
                navItems.Add(new("employees", "Access & Employees", "/employees", "users", UiPermissions.EmployeeManage));
            }

            if (canViewHsmStatus)
            {
                navItems.Add(new("hsm", "FIPS HSM Vault", "/hsm", "shield", UiPermissions.HsmRead, IsAdminOnly: true));
            }

            if (canViewAuditLogs)
            {
                navItems.Add(new("audit", "Security Audit Logs", "/audit", "file-text", UiPermissions.AuditRead, IsAdminOnly: true));
            }

            // Construct fine-grained action capabilities
            var actionCapabilities = new Dictionary<string, UiActionCapability>(StringComparer.OrdinalIgnoreCase)
            {
                ["memory.create"] = new("memory.create", IsVisible: true, IsEnabled: true),
                ["memory.delete"] = new("memory.delete", IsVisible: canDeleteMemory, IsEnabled: canDeleteMemory,
                    RestrictionReason: canDeleteMemory ? null : "Requires TenantAdmin or memory:delete permission"),
                ["memory.purge"] = new("memory.purge", IsVisible: canPurgeMemories, IsEnabled: canPurgeMemories,
                    RestrictionReason: canPurgeMemories ? null : "Requires SuperAdmin role"),

                ["provider.connect"] = new("provider.connect", IsVisible: canManageConnections, IsEnabled: canManageConnections,
                    RestrictionReason: canManageConnections ? null : "Requires TenantAdmin or provider:connect permission"),
                ["provider.revoke"] = new("provider.revoke", IsVisible: canRevokeConnections, IsEnabled: canRevokeConnections,
                    RestrictionReason: canRevokeConnections ? null : "Requires TenantAdmin or provider:revoke permission"),
                ["provider.test"] = new("provider.test", IsVisible: true, IsEnabled: true),

                ["hsm.status"] = new("hsm.status", IsVisible: canViewHsmStatus, IsEnabled: canViewHsmStatus,
                    RestrictionReason: canViewHsmStatus ? null : "Restricted to authorized security personnel"),
                ["hsm.rotate"] = new("hsm.rotate", IsVisible: canRotateHsmKeys, IsEnabled: canRotateHsmKeys,
                    RestrictionReason: canRotateHsmKeys ? null : "Restricted to SuperAdmin / Cloud HSM Cryptographic Officer"),

                ["audit.view"] = new("audit.view", IsVisible: canViewAuditLogs, IsEnabled: canViewAuditLogs,
                    RestrictionReason: canViewAuditLogs ? null : "Requires Security Officer or Auditor role")
            };

            return new UiCapabilitiesDescriptor(
                UserId: userId,
                TenantId: tenantId,
                Roles: roles,
                Permissions: permissions.ToList(),
                CanCreateMemory: canCreateMemory,
                CanDeleteMemory: canDeleteMemory,
                CanPurgeMemories: canPurgeMemories,
                CanManageConnections: canManageConnections,
                CanRevokeConnections: canRevokeConnections,
                CanTestConnections: canTestConnections,
                CanViewHsmStatus: canViewHsmStatus,
                CanRotateHsmKeys: canRotateHsmKeys,
                CanViewAuditLogs: canViewAuditLogs,
                CanManageEmployees: canManageEmployees,
                VisibleNavigation: navItems,
                ActionCapabilities: actionCapabilities
            );
        }

        private static UiCapabilitiesDescriptor CreateAnonymousCapabilities()
        {
            return new UiCapabilitiesDescriptor(
                UserId: "anonymous",
                TenantId: "none",
                Roles: Array.Empty<string>(),
                Permissions: Array.Empty<string>(),
                CanCreateMemory: false,
                CanDeleteMemory: false,
                CanPurgeMemories: false,
                CanManageConnections: false,
                CanRevokeConnections: false,
                CanTestConnections: false,
                CanViewHsmStatus: false,
                CanRotateHsmKeys: false,
                CanViewAuditLogs: false,
                CanManageEmployees: false,
                VisibleNavigation: Array.Empty<NavigationItem>(),
                ActionCapabilities: new Dictionary<string, UiActionCapability>()
            );
        }
    }
}
