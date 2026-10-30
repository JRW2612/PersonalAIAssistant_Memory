using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using PersonalAIAssistant.Memory.Api.Controllers;
using PersonalAIAssistant.Memory.Api.Security;
using PersonalAIAssistant.Memory.Business.Security;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security;

/// <summary>
/// Integration and unit tests verifying Trusted Tenant Identity, Tenant-Scoped Queries,
/// Authorization Policies, and Security Audit Trails across tenant boundaries.
/// </summary>
public class MultiTenantIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string JwtSecret = "SuperSecretJwtAuthenticationSigningKey32BytesLongStringForHmacSha256!";
    private const string Issuer = "PersonalAIAssistant.Memory";
    private const string Audience = "PersonalAIAssistant.Memory.Api";

    public MultiTenantIsolationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryStore", "true");
            builder.UseSetting("Jwt:SecretKey", JwtSecret);
        });
    }

    private string GenerateTenantJwt(
        string userId,
        string tenantId,
        string role,
        params (string Type, string Value)[] additionalClaims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("sub", userId),
            new("tenant_id", tenantId),
            new("tid", tenantId),
            new(ClaimTypes.Role, role)
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
    public async Task GetById_CrossTenantAttempt_Returns403Forbidden()
    {
        // Arrange: Alice in tenant-alpha attempts to read an employee in tenant-beta
        var client = _factory.CreateClient();
        var targetRecordId = Guid.NewGuid();

        await SeedEmployeeAsync(new EmployeeRecord
        {
            Id = targetRecordId,
            UserId = "emp-charlie",
            TenantId = "tenant-beta",
            FullName = "Charlie Davis",
            Department = "R&D",
            Salary = 160000
        });

        var aliceToken = GenerateTenantJwt("emp-alice", "tenant-alpha", "Employee");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);

        // Act
        var response = await client.GetAsync($"/api/v1/employees/{targetRecordId}");

        // Assert: Boundary isolation strictly enforced
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_CrossTenantAttempt_Returns403Forbidden()
    {
        // Arrange: User from tenant-alpha attempts to update record in tenant-beta
        var client = _factory.CreateClient();
        var targetRecordId = Guid.NewGuid();

        await SeedEmployeeAsync(new EmployeeRecord
        {
            Id = targetRecordId,
            UserId = "emp-charlie",
            TenantId = "tenant-beta",
            FullName = "Charlie Davis",
            Department = "R&D"
        });

        var aliceToken = GenerateTenantJwt("emp-alice", "tenant-alpha", "Employee");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);

        var updateDto = new UpdateEmployeeDto(targetRecordId, "Malicious Tamper", "R&D");

        // Act
        var response = await client.PutAsJsonAsync($"/api/v1/employees/{targetRecordId}", updateDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_CrossTenantAttempt_EvenWithAdminDeletePermissions_Returns403Forbidden()
    {
        // Arrange: Admin from tenant-alpha with explicit delete permission attempts to delete tenant-beta record
        var client = _factory.CreateClient();
        var targetRecordId = Guid.NewGuid();

        await SeedEmployeeAsync(new EmployeeRecord
        {
            Id = targetRecordId,
            UserId = "emp-charlie",
            TenantId = "tenant-beta",
            FullName = "Charlie Davis",
            Department = "R&D"
        });

        var adminAlphaToken = GenerateTenantJwt(
            "admin-alpha",
            "tenant-alpha",
            "Admin",
            (AppPermissions.ClaimType, AppPermissions.DeleteEmployees),
            (AppPermissions.ClaimType, AppPermissions.ManageEmployees));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminAlphaToken);

        // Act
        var response = await client.DeleteAsync($"/api/v1/employees/{targetRecordId}");

        // Assert: Cross-tenant administrative access is rejected by isolation policy
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAll_TenantScopedQuery_ReturnsOnlyCallerTenantRecords()
    {
        // Arrange: Seed records across tenant-omega and tenant-gamma
        var client = _factory.CreateClient();
        var omegaRecord = new EmployeeRecord
        {
            Id = Guid.NewGuid(),
            UserId = "emp-omega-1",
            TenantId = "tenant-omega",
            FullName = "Omega Specialist",
            Department = "Security"
        };
        var gammaRecord = new EmployeeRecord
        {
            Id = Guid.NewGuid(),
            UserId = "emp-gamma-1",
            TenantId = "tenant-gamma",
            FullName = "Gamma Specialist",
            Department = "Operations"
        };

        await SeedEmployeeAsync(omegaRecord);
        await SeedEmployeeAsync(gammaRecord);

        var omegaToken = GenerateTenantJwt("emp-omega-1", "tenant-omega", "Employee");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", omegaToken);

        // Act
        var response = await client.GetAsync("/api/v1/employees");

        // Assert: Only tenant-omega records returned, zero leakage of tenant-gamma data
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var employees = await response.Content.ReadFromJsonAsync<List<EmployeeRecord>>();
        employees.Should().NotBeNull();
        employees.Should().Contain(e => e.Id == omegaRecord.Id);
        employees.Should().NotContain(e => e.Id == gammaRecord.Id);
        employees!.All(e => e.TenantId == "tenant-omega").Should().BeTrue();
    }

    [Fact]
    public async Task TenantSpoofingResistance_HeaderAndQueryParametersIgnored()
    {
        // Arrange: Caller holds token for tenant-omega, but tries to inject tenant-gamma via header and query
        var client = _factory.CreateClient();
        var omegaRecord = new EmployeeRecord
        {
            Id = Guid.NewGuid(),
            UserId = "emp-omega-spoof",
            TenantId = "tenant-omega",
            FullName = "Omega Spoof Test",
            Department = "Security"
        };
        var gammaRecord = new EmployeeRecord
        {
            Id = Guid.NewGuid(),
            UserId = "emp-gamma-target",
            TenantId = "tenant-gamma",
            FullName = "Gamma Secret Record",
            Department = "Treasury"
        };

        await SeedEmployeeAsync(omegaRecord);
        await SeedEmployeeAsync(gammaRecord);

        var omegaToken = GenerateTenantJwt("emp-omega-spoof", "tenant-omega", "Employee");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", omegaToken);
        client.DefaultRequestHeaders.Add("X-Tenant-ID", "tenant-gamma"); // Malicious header attempt

        // Act: Attempt to pass query param as well
        var response = await client.GetAsync("/api/v1/employees?tenantId=tenant-gamma");

        // Assert: Server relies exclusively on validated JWT claims, ignoring client-supplied tenant overrides
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var employees = await response.Content.ReadFromJsonAsync<List<EmployeeRecord>>();
        employees.Should().NotBeNull();
        employees.Should().NotContain(e => e.Id == gammaRecord.Id);
        employees!.All(e => e.TenantId == "tenant-omega").Should().BeTrue();
    }

    [Fact]
    public async Task EmployeeAuthorizationHandler_CrossTenantAttempt_InvokesAuditLogger()
    {
        // Arrange
        var auditLoggerMock = new Mock<ISecurityAuditLogger>();
        var handler = new EmployeeAuthorizationHandler(auditLoggerMock.Object);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "attacker-user"),
            new Claim("tenant_id", "tenant-attacker")
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = new AuthorizationHandlerContext(
            new[] { EmployeeOperations.Read },
            user,
            new EmployeeRecord
            {
                Id = Guid.NewGuid(),
                UserId = "victim-user",
                TenantId = "tenant-victim"
            });

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
        auditLoggerMock.Verify(a => a.LogCrossTenantAttempt(
            "attacker-user",
            "tenant-attacker",
            It.IsAny<string>(),
            "tenant-victim",
            "Employee:Read"), Times.Once);
    }

    [Fact]
    public async Task AuthorizationBehavior_CrossTenantMemoryAccess_ThrowsUnauthorizedAndEmitsAudit()
    {
        // Arrange
        var readRepoMock = new Mock<IReadModelRepository>();
        var auditLoggerMock = new Mock<ISecurityAuditLogger>();

        var memoryId = Guid.NewGuid();
        var targetMemory = new MemoryReadModel
        {
            MemoryId = memoryId,
            UserId = "user-victim",
            TenantId = "tenant-enterprise-vault",
            Summary = "Confidential business trade secret"
        };

        readRepoMock.Setup(r => r.GetMemoriesByIdsAsync(
                It.Is<IEnumerable<Guid>>(ids => ids.Contains(memoryId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { targetMemory });

        var behavior = new AuthorizationBehavior<TestAuthorizedRequest, string>(
            readRepoMock.Object,
            NullLogger<AuthorizationBehavior<TestAuthorizedRequest, string>>.Instance,
            auditLoggerMock.Object);

        var request = new TestAuthorizedRequest(
            MemoryId: memoryId,
            UserId: "user-attacker",
            TenantId: "tenant-attacker");

        // Act
        RequestHandlerDelegate<string> next = (ct) => Task.FromResult("succeeded");
        var act = () => behavior.Handle(request, next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*Cross-tenant access denied*");

        auditLoggerMock.Verify(a => a.LogCrossTenantAttempt(
            "user-attacker",
            "tenant-attacker",
            memoryId.ToString(),
            "tenant-enterprise-vault",
            nameof(TestAuthorizedRequest)), Times.Once);
    }

    private async Task SeedEmployeeAsync(EmployeeRecord record)
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IEmployeeRepository>();
        await repo.AddAsync(record);
    }

    private record TestAuthorizedRequest(Guid MemoryId, string UserId, string TenantId) : IAuthorizedRequest;
}
