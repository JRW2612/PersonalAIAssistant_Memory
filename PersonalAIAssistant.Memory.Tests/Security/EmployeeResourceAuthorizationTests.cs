using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PersonalAIAssistant.Memory.Api.Controllers;
using PersonalAIAssistant.Memory.Api.Security;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security;

public class EmployeeResourceAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string JwtSecret = "SuperSecretJwtAuthenticationSigningKey32BytesLongStringForHmacSha256!";
    private const string Issuer = "PersonalAIAssistant.Memory";
    private const string Audience = "PersonalAIAssistant.Memory.Api";

    public EmployeeResourceAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryStore", "true");
        });
    }

    private string GenerateJwt(string userId, string role, params (string Type, string Value)[] additionalClaims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("sub", userId),
            new(ClaimTypes.Role, role),
            new("tenant_id", "default")
        };

        foreach (var claim in additionalClaims)
        {
            claims.Add(new Claim(claim.Type, claim.Value));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task GetById_EmployeeAccessingOwnRecord_Returns200Ok()
    {
        // Arrange
        var client = _factory.CreateClient();
        var recordId = Guid.NewGuid();
        var userId = "emp-alice";

        await SeedEmployeeAsync(new EmployeeRecord
        {
            Id = recordId,
            UserId = userId,
            FullName = "Alice Smith",
            Department = "Engineering",
            Salary = 135000
        });

        var token = GenerateJwt(userId, "Employee");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync($"/api/v1/employees/{recordId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<EmployeeRecord>();
        Assert.NotNull(result);
        Assert.Equal("Alice Smith", result.FullName);
    }

    [Fact]
    public async Task GetById_EmployeeAttemptingToReadOtherEmployeeRecord_Returns403Forbidden()
    {
        // Arrange: Bob's record exists, Alice authenticates and attempts to read it
        var client = _factory.CreateClient();
        var bobRecordId = Guid.NewGuid();

        await SeedEmployeeAsync(new EmployeeRecord
        {
            Id = bobRecordId,
            UserId = "emp-bob",
            FullName = "Bob Jones",
            Department = "Finance",
            Salary = 95000
        });

        var aliceToken = GenerateJwt("emp-alice", "Employee");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);

        // Act
        var response = await client.GetAsync($"/api/v1/employees/{bobRecordId}");

        // Assert: Resource-based authorization Handler denies cross-employee access
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_TamperedBodyIdMismatch_Returns400BadRequest()
    {
        // Arrange: Alice provides her ID in the URL, but attempts to change Bob's record via body
        var client = _factory.CreateClient();
        var aliceRecordId = Guid.NewGuid();
        var bobRecordId = Guid.NewGuid();

        var aliceToken = GenerateJwt("emp-alice", "Employee");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);

        var payload = new UpdateEmployeeDto(bobRecordId, "Malicious Update", "Engineering");

        // Act
        var response = await client.PutAsJsonAsync($"/api/v1/employees/{aliceRecordId}", payload);

        // Assert: Route vs Body parameter tampering detected and rejected
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_EmployeeAttemptingToUpdateOtherEmployeeRecord_Returns403Forbidden()
    {
        // Arrange: Alice attempts to update Bob's record targeting Bob's ID in both URL and body
        var client = _factory.CreateClient();
        var bobRecordId = Guid.NewGuid();

        await SeedEmployeeAsync(new EmployeeRecord
        {
            Id = bobRecordId,
            UserId = "emp-bob",
            FullName = "Bob Jones",
            Department = "Finance"
        });

        var aliceToken = GenerateJwt("emp-alice", "Employee");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);

        var payload = new UpdateEmployeeDto(bobRecordId, "Hacked FullName", "Finance");

        // Act
        var response = await client.PutAsJsonAsync($"/api/v1/employees/{bobRecordId}", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AdminWithoutExplicitPermission_Returns403Forbidden()
    {
        // Arrange: Admin role alone without explicit "employees:delete" claim must be denied
        var client = _factory.CreateClient();
        var targetRecordId = Guid.NewGuid();

        await SeedEmployeeAsync(new EmployeeRecord
        {
            Id = targetRecordId,
            UserId = "emp-charlie",
            FullName = "Charlie"
        });

        var adminTokenWithoutClaim = GenerateJwt("admin-user", "Admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminTokenWithoutClaim);

        // Act
        var response = await client.DeleteAsync($"/api/v1/employees/{targetRecordId}");

        // Assert: Least privilege enforced (blanket admin rejected)
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AdminWithExplicitPermission_Returns204NoContent()
    {
        // Arrange: Admin has explicit "employees:delete" permission claim
        var client = _factory.CreateClient();
        var targetRecordId = Guid.NewGuid();

        await SeedEmployeeAsync(new EmployeeRecord
        {
            Id = targetRecordId,
            UserId = "emp-charlie",
            FullName = "Charlie"
        });

        var adminWithPermissionToken = GenerateJwt(
            "admin-user", 
            "Admin", 
            (AppPermissions.ClaimType, AppPermissions.DeleteEmployees));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminWithPermissionToken);

        // Act
        var response = await client.DeleteAsync($"/api/v1/employees/{targetRecordId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedRequest_Returns401Unauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var recordId = Guid.NewGuid();

        // Act: No bearer token provided
        var response = await client.GetAsync($"/api/v1/employees/{recordId}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task SeedEmployeeAsync(EmployeeRecord record)
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IEmployeeRepository>();
        await repo.AddAsync(record);
    }
}
