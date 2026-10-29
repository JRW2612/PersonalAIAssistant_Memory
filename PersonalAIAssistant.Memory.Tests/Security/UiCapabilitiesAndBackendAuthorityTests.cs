using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PersonalAIAssistant.Memory.Api.Controllers;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models.Security;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security
{
    public class UiCapabilitiesAndBackendAuthorityTests
    {
        private readonly UiCapabilitiesService _capabilitiesService;
        private readonly Mock<ISecurityAuditLogger> _auditLoggerMock;

        public UiCapabilitiesAndBackendAuthorityTests()
        {
            _capabilitiesService = new UiCapabilitiesService();
            _auditLoggerMock = new Mock<ISecurityAuditLogger>();
        }

        private static ClaimsPrincipal CreatePrincipal(string userId, string tenantId, string role, params string[] permissions)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId),
                new("sub", userId),
                new("tenant_id", tenantId),
                new("tid", tenantId),
                new(ClaimTypes.Role, role)
            };

            foreach (var perm in permissions)
            {
                claims.Add(new Claim("permission", perm));
            }

            var identity = new ClaimsIdentity(claims, "TestAuth");
            return new ClaimsPrincipal(identity);
        }

        [Fact]
        public void BuildCapabilities_StandardEmployee_HidesAdministrativeControls()
        {
            // Arrange
            var user = CreatePrincipal("emp-alice", "tenant-hdfc", "Employee");

            // Act
            var capabilities = _capabilitiesService.BuildCapabilities(user);

            // Assert
            capabilities.UserId.Should().Be("emp-alice");
            capabilities.TenantId.Should().Be("tenant-hdfc");
            capabilities.CanCreateMemory.Should().BeTrue();
            capabilities.CanDeleteMemory.Should().BeFalse("Standard employees cannot delete memories");
            capabilities.CanRevokeConnections.Should().BeFalse("Standard employees cannot revoke cloud providers");
            capabilities.CanViewHsmStatus.Should().BeFalse("HSM vault details are restricted to security officers");
            capabilities.CanRotateHsmKeys.Should().BeFalse("Key rotation is restricted to cryptographic officers");
            capabilities.CanViewAuditLogs.Should().BeFalse("Audit trails are restricted to auditors/admins");

            // Verify navigation items: Only memories and connections visible
            capabilities.VisibleNavigation.Should().Contain(n => n.Id == "memories");
            capabilities.VisibleNavigation.Should().Contain(n => n.Id == "connections");
            capabilities.VisibleNavigation.Should().NotContain(n => n.Id == "hsm");
            capabilities.VisibleNavigation.Should().NotContain(n => n.Id == "audit");
            capabilities.VisibleNavigation.Should().NotContain(n => n.Id == "employees");
        }

        [Fact]
        public void BuildCapabilities_TenantAdmin_EnablesTenantLevelAdministrativeControls()
        {
            // Arrange
            var admin = CreatePrincipal("admin-bob", "tenant-icici", "TenantAdmin");

            // Act
            var capabilities = _capabilitiesService.BuildCapabilities(admin);

            // Assert
            capabilities.UserId.Should().Be("admin-bob");
            capabilities.TenantId.Should().Be("tenant-icici");
            capabilities.CanDeleteMemory.Should().BeTrue();
            capabilities.CanManageConnections.Should().BeTrue();
            capabilities.CanRevokeConnections.Should().BeTrue();
            capabilities.CanViewHsmStatus.Should().BeTrue();
            capabilities.CanViewAuditLogs.Should().BeTrue();
            capabilities.CanManageEmployees.Should().BeTrue();
            capabilities.CanRotateHsmKeys.Should().BeFalse("Only SuperAdmin can rotate HSM Master Keys");

            capabilities.VisibleNavigation.Should().Contain(n => n.Id == "hsm");
            capabilities.VisibleNavigation.Should().Contain(n => n.Id == "audit");
            capabilities.VisibleNavigation.Should().Contain(n => n.Id == "employees");
        }

        [Fact]
        public void BuildCapabilities_SuperAdmin_EnablesAllCapabilities()
        {
            // Arrange
            var superAdmin = CreatePrincipal("ciso-user", "system", "SuperAdmin");

            // Act
            var capabilities = _capabilitiesService.BuildCapabilities(superAdmin);

            // Assert
            capabilities.CanPurgeMemories.Should().BeTrue();
            capabilities.CanRotateHsmKeys.Should().BeTrue();
            capabilities.CanRevokeConnections.Should().BeTrue();
            capabilities.CanViewAuditLogs.Should().BeTrue();
        }

        [Fact]
        public void BackendAuthority_WhenFrontendTampered_RejectsUnauthorizedActionWith403Forbidden()
        {
            // Arrange: Attacker modifies DOM in DevTools to unhide and invoke 'hsm.rotate' as standard employee
            var employeeUser = CreatePrincipal("emp-attacker", "tenant-hdfc", "Employee");

            var controller = new UiNavigationController(_capabilitiesService, _auditLoggerMock.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = employeeUser }
                }
            };

            var tamperRequest = new UiNavigationController.TamperSimulationRequest(
                TargetAction: "hsm.rotate",
                TargetResourceId: "hsm-kek-001");

            // Act
            var result = controller.SimulateTamperAttempt(tamperRequest);

            // Assert: Backend remains strictly authoritative and returns 403 Forbidden
            var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
            objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

            // Verify security audit logger was notified
            _auditLoggerMock.Verify(x => x.LogAccessDenied(
                "emp-attacker",
                "hsm.rotate",
                "hsm-kek-001",
                It.Is<string>(s => s.Contains("SuperAdmin"))), Times.Once);
        }

        [Fact]
        public void BackendAuthority_WhenCrossTenantTampered_RejectsWith403ForbiddenAndLogsThreat()
        {
            // Arrange: Attacker in tenant-hdfc attempts to access resource in tenant-sbi
            var employeeUser = CreatePrincipal("emp-attacker", "tenant-hdfc", "Employee");

            var controller = new UiNavigationController(_capabilitiesService, _auditLoggerMock.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = employeeUser }
                }
            };

            var tamperRequest = new UiNavigationController.TamperSimulationRequest(
                TargetAction: "memory.read",
                TargetResourceId: "mem-secret-456",
                TargetTenantId: "tenant-sbi");

            // Act
            var result = controller.SimulateTamperAttempt(tamperRequest);

            // Assert: Backend blocks cross-tenant access with 403 Forbidden
            var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
            objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

            // Verify cross-tenant security audit was triggered
            _auditLoggerMock.Verify(x => x.LogCrossTenantAttempt(
                "emp-attacker",
                "tenant-hdfc",
                "mem-secret-456",
                "tenant-sbi",
                "memory.read"), Times.Once);
        }
    }
}
