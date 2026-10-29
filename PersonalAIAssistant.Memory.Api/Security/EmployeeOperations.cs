using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace PersonalAIAssistant.Memory.Api.Security;

public static class EmployeeOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };
    public static readonly OperationAuthorizationRequirement Update = new() { Name = nameof(Update) };
    public static readonly OperationAuthorizationRequirement Delete = new() { Name = nameof(Delete) };
}

public static class AppPermissions
{
    public const string ClaimType = "permission";
    public const string ManageEmployees = "employees:manage";
    public const string ReadAllEmployees = "employees:read:all";
    public const string DeleteEmployees = "employees:delete";
}
