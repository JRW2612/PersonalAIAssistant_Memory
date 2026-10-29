namespace PersonalAIAssistant.Memory.Core.Models.Security
{
    public static class UiPermissions
    {
        public const string MemoryRead = "memory:read";
        public const string MemoryWrite = "memory:write";
        public const string MemoryDelete = "memory:delete";
        public const string MemoryPurge = "memory:purge";

        public const string ProviderConnect = "provider:connect";
        public const string ProviderRevoke = "provider:revoke";
        public const string ProviderTest = "provider:test";

        public const string HsmRead = "hsm:read";
        public const string HsmRotate = "hsm:rotate";
        public const string HsmRevoke = "hsm:revoke";

        public const string AuditRead = "audit:read";
        public const string AuditExport = "audit:export";

        public const string EmployeeManage = "employees:manage";
        public const string EmployeeDelete = "employees:delete";
    }

    public record NavigationItem(
        string Id,
        string Label,
        string Path,
        string Icon,
        string RequiredPermission,
        bool IsAdminOnly = false);

    public record UiActionCapability(
        string ActionKey,
        bool IsVisible,
        bool IsEnabled,
        string? RestrictionReason = null);

    public record UiCapabilitiesDescriptor(
        string UserId,
        string TenantId,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Permissions,
        bool CanCreateMemory,
        bool CanDeleteMemory,
        bool CanPurgeMemories,
        bool CanManageConnections,
        bool CanRevokeConnections,
        bool CanTestConnections,
        bool CanViewHsmStatus,
        bool CanRotateHsmKeys,
        bool CanViewAuditLogs,
        bool CanManageEmployees,
        IReadOnlyList<NavigationItem> VisibleNavigation,
        IReadOnlyDictionary<string, UiActionCapability> ActionCapabilities);
}
