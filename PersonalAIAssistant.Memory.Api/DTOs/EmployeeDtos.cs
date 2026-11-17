using System.Text.Json.Serialization;

namespace PersonalAIAssistant.Memory.Api.DTOs;

/// <summary>
/// Data Transfer Object representing an employee record.
/// Prevents over-exposure of sensitive internal entities by stripping internal database metadata
/// and masking or suppressing PII/financial data (Salary, SSN) according to caller privilege.
/// </summary>
public record EmployeeResponseDto(
    Guid Id,
    string UserId,
    string TenantId,
    string FullName,
    string Department,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Salary,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SocialSecurityNumber
);
