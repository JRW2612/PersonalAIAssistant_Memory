namespace PersonalAIAssistant.Memory.Core.Models;

public class EmployeeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = "default";
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public decimal Salary { get; set; }
    public string SocialSecurityNumber { get; set; } = string.Empty;
}
