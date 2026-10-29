using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalAIAssistant.Memory.Api.Security;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;

namespace PersonalAIAssistant.Memory.Api.Controllers;

public record UpdateEmployeeDto(Guid Id, string FullName, string Department);

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class EmployeesController : ControllerBase
{
    private readonly IAuthorizationService _authorizationService;
    private readonly IEmployeeRepository _repository;

    public EmployeesController(
        IAuthorizationService authorizationService,
        IEmployeeRepository repository)
    {
        _authorizationService = authorizationService;
        _repository = repository;
    }

    /// <summary>
    /// Retrieves all employee records scoped strictly to the authenticated caller's tenant.
    /// Trusted Tenant Identity: Tenant ID is extracted exclusively from validated claims, never from client parameters.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<EmployeeRecord>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var callerTenantId = User.FindFirstValue("tid")
                          ?? User.FindFirstValue("tenant_id")
                          ?? User.FindFirstValue("tenantid")
                          ?? "default";

        var records = await _repository.GetByTenantAsync(callerTenantId, ct);
        return Ok(records);
    }

    /// <summary>
    /// Gets an employee record by ID with server-side resource-based authorization.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EmployeeRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var record = await _repository.GetByIdAsync(id, ct);
        if (record is null)
        {
            return NotFound(new { error = $"Employee record '{id}' was not found." });
        }

        var authResult = await _authorizationService.AuthorizeAsync(User, record, EmployeeOperations.Read);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        return Ok(record);
    }

    /// <summary>
    /// Updates an employee record by ID, checking route/body ID consistency and resource authorization.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEmployeeDto dto, CancellationToken ct)
    {
        // Parameter tampering defense: Route ID and Body ID must match
        if (id != dto.Id)
        {
            return BadRequest(new { error = "Route ID does not match the payload ID." });
        }

        var record = await _repository.GetByIdAsync(id, ct);
        if (record is null)
        {
            return NotFound(new { error = $"Employee record '{id}' was not found." });
        }

        var authResult = await _authorizationService.AuthorizeAsync(User, record, EmployeeOperations.Update);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        record.FullName = dto.FullName;
        record.Department = dto.Department;
        await _repository.UpdateAsync(record, ct);

        return NoContent();
    }

    /// <summary>
    /// Deletes an employee record by ID, requiring explicit administrative delete permission.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var record = await _repository.GetByIdAsync(id, ct);
        if (record is null)
        {
            return NotFound(new { error = $"Employee record '{id}' was not found." });
        }

        var authResult = await _authorizationService.AuthorizeAsync(User, record, EmployeeOperations.Delete);
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        await _repository.DeleteAsync(id, ct);
        return NoContent();
    }
}
