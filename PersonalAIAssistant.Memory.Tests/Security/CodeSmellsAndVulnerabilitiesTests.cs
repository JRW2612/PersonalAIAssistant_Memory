using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalAIAssistant.Memory.Api.Controllers;
using PersonalAIAssistant.Memory.Api.DTOs;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Business.Handlers;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.AI;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security;

/// <summary>
/// Unit tests verifying the elimination of 5 critical security and architectural code smells:
/// 1. IDOR / Broken Object-Level Authorization (BOLA)
/// 2. N+1 Query Problem
/// 3. Missing Cancellation & Timeouts
/// 4. Swallowed Exceptions
/// 5. Over-Exposed Data Entities
/// </summary>
public class CodeSmellsAndVulnerabilitiesTests
{
    // =========================================================================
    // 1. IDOR / Broken Object-Level Authorization (BOLA) Tests
    // =========================================================================

    [Fact]
    public async Task ProviderConnections_UnauthenticatedCaller_ThrowsUnauthorizedAccessException_NeverDefaultsToAliceOrClientHeader()
    {
        // Arrange
        var repoMock = new Mock<IProviderConnectionRepository>();
        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.UserId).Returns(string.Empty); // Unauthenticated
        userContextMock.Setup(u => u.IsAdmin).Returns(false);

        var controller = new ProviderConnectionsController(
            repoMock.Object,
            Mock.Of<IProviderTokenService>(),
            Mock.Of<IOAuthStateManager>(),
            Array.Empty<IOAuthProviderHandler>(),
            userContextMock.Object,
            Mock.Of<ISecurityAuditLogger>(),
            Mock.Of<IAIProviderFactory>(),
            NullLogger<ProviderConnectionsController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-User-Id"] = "spoofed-user-id";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act & Assert: Must reject unauthenticated request rather than trusting header or fallback "user-alice"
        var act = () => controller.GetConnections(CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*could not be verified*");
    }

    [Fact]
    public async Task ProviderConnections_NonAdminCaller_IgnoresSpoofedXUserIdHeader()
    {
        // Arrange: User Bob calls the endpoint but sends X-User-Id: alice
        var repoMock = new Mock<IProviderConnectionRepository>();
        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.UserId).Returns("user-bob");
        userContextMock.Setup(u => u.IsAdmin).Returns(false);

        repoMock.Setup(r => r.GetUserConnectionsAsync("user-bob", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProviderConnectionEntity>());

        var controller = new ProviderConnectionsController(
            repoMock.Object,
            Mock.Of<IProviderTokenService>(),
            Mock.Of<IOAuthStateManager>(),
            Array.Empty<IOAuthProviderHandler>(),
            userContextMock.Object,
            Mock.Of<ISecurityAuditLogger>(),
            Mock.Of<IAIProviderFactory>(),
            NullLogger<ProviderConnectionsController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-User-Id"] = "victim-alice";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        var result = await controller.GetConnections(CancellationToken.None);

        // Assert: Resolved user must be Bob (from token), never Alice (from header)
        result.Should().BeOfType<OkObjectResult>();
        repoMock.Verify(r => r.GetUserConnectionsAsync("user-bob", It.IsAny<CancellationToken>()), Times.Once);
        repoMock.Verify(r => r.GetUserConnectionsAsync("victim-alice", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WorkloadsController_RegisterWorkload_CrossTenantAttemptByNonAdmin_Returns403Forbidden()
    {
        // Arrange: Caller is in tenant-alpha, attempts to register workload in tenant-beta
        var repoMock = new Mock<IWorkloadIdentityRepository>();
        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.UserId).Returns("emp-user-1");
        userContextMock.Setup(u => u.TenantId).Returns("tenant-alpha");
        userContextMock.Setup(u => u.IsAdmin).Returns(false);

        var controller = new WorkloadsController(
            repoMock.Object,
            Mock.Of<IWorkloadAuthenticationService>(),
            Mock.Of<IProviderTokenService>(),
            userContextMock.Object,
            Mock.Of<ISecurityAuditLogger>(),
            new ConfigurationBuilder().Build(),
            NullLogger<WorkloadsController>.Instance);

        var request = new RegisterWorkloadRequestDto(
            ClientId: "service-worker",
            Name: "Worker Service",
            CloudPlatform: "AWS",
            AuthType: "OidcWorkloadIdentity",
            TenantId: "tenant-beta", // Attacking cross-tenant
            AllowedScopes: "ai.memory.read",
            ClientSecret: null,
            OidcIssuer: null,
            OidcAudience: null,
            OidcSubjectFilter: null,
            WebhookSecret: null
        );

        // Act
        var result = await controller.RegisterWorkload(request, CancellationToken.None);

        // Assert: IDOR defense strictly returns 403 Forbidden
        result.Should().BeOfType<ForbidResult>();
        repoMock.Verify(r => r.UpsertAsync(It.IsAny<WorkloadIdentityEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WorkloadsController_RevokeWorkload_CrossTenantAttemptByNonAdmin_Returns403Forbidden()
    {
        // Arrange: Existing workload belongs to tenant-beta, caller is in tenant-alpha
        var repoMock = new Mock<IWorkloadIdentityRepository>();
        repoMock.Setup(r => r.GetByClientIdAsync("target-client", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkloadIdentityEntity
            {
                ClientId = "target-client",
                TenantId = "tenant-beta"
            });

        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.UserId).Returns("attacker");
        userContextMock.Setup(u => u.TenantId).Returns("tenant-alpha");
        userContextMock.Setup(u => u.IsAdmin).Returns(false);

        var controller = new WorkloadsController(
            repoMock.Object,
            Mock.Of<IWorkloadAuthenticationService>(),
            Mock.Of<IProviderTokenService>(),
            userContextMock.Object,
            Mock.Of<ISecurityAuditLogger>(),
            new ConfigurationBuilder().Build(),
            NullLogger<WorkloadsController>.Instance);

        // Act
        var result = await controller.RevokeWorkload("target-client", new RevokeWorkloadRequestDto("Malicious delete"), CancellationToken.None);

        // Assert
        result.Should().BeOfType<ForbidResult>();
        repoMock.Verify(r => r.RevokeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AccessAssignments_CreateAssignment_NonAdminTargetingAnotherUser_Returns403Forbidden()
    {
        // Arrange: Caller is user-alice, attempts to create assignment for user-bob
        var repoMock = new Mock<IAccessAssignmentRepository>();
        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.UserId).Returns("user-alice");
        userContextMock.Setup(u => u.IsAdmin).Returns(false);
        userContextMock.Setup(u => u.Roles).Returns(new List<string> { "StandardUser" });

        var controller = new AccessAssignmentsController(
            repoMock.Object,
            userContextMock.Object,
            NullLogger<AccessAssignmentsController>.Instance);

        var request = new CreateAssignmentRequest("user-bob", "resource-top-secret", "Admin");

        // Act
        var result = await controller.CreateAssignment(request, CancellationToken.None);

        // Assert: BOLA defense denies cross-user permission grants
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task AccessAssignments_GetStatus_NonAdminViewingOtherUserAssignment_Returns403Forbidden()
    {
        // Arrange: Assignment belongs to user-bob, caller is user-alice
        var assignmentId = Guid.NewGuid();
        var repoMock = new Mock<IAccessAssignmentRepository>();
        repoMock.Setup(r => r.GetByIdAsync(assignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessAssignmentEntity
            {
                Id = assignmentId,
                UserId = "user-bob",
                TenantId = "default"
            });

        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.UserId).Returns("user-alice");
        userContextMock.Setup(u => u.IsAdmin).Returns(false);
        userContextMock.Setup(u => u.Roles).Returns(new List<string> { "StandardUser" });

        var controller = new AccessAssignmentsController(
            repoMock.Object,
            userContextMock.Object,
            NullLogger<AccessAssignmentsController>.Instance);

        // Act
        var result = await controller.GetStatus(assignmentId, CancellationToken.None);

        // Assert: BOLA defense prevents viewing other user's access assignments
        result.Should().BeOfType<ForbidResult>();
    }

    // =========================================================================
    // 2. N+1 Query Problem Tests
    // =========================================================================

    [Fact]
    public async Task SupportController_GetTenantUsage_BatchQueriesAiMetricsInSingleCall()
    {
        // Arrange: 5 workloads registered for tenant-alpha
        var workloadRepoMock = new Mock<IWorkloadIdentityRepository>();
        var aiMetricsMock = new Mock<IAiMetricsLogger>();
        var readRepoMock = new Mock<IReadModelRepository>();
        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.UserId).Returns("support-agent-1");

        var workloads = Enumerable.Range(1, 5).Select(i => new WorkloadIdentityEntity
        {
            ClientId = $"workload-client-{i}",
            TenantId = "tenant-alpha"
        }).ToList();

        workloadRepoMock.Setup(w => w.GetByTenantIdAsync("tenant-alpha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workloads);

        aiMetricsMock.Setup(m => m.GetBatchSummaryAsync(
                It.Is<IEnumerable<string>>(ids => ids.Count() == 6), // 5 workloads + tenant-alpha
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiUsageSummary(TotalPromptTokens: 500, TotalCompletionTokens: 250, TotalEstimatedCostUsd: 0.05, CallCount: 10));

        var controller = new SupportController(
            aiMetricsMock.Object,
            Mock.Of<IAgentTraceRepository>(),
            readRepoMock.Object,
            workloadRepoMock.Object,
            Mock.Of<ISecurityAuditLogger>(),
            userContextMock.Object,
            Mock.Of<IServiceProvider>(),
            NullLogger<SupportController>.Instance);

        // Act
        var result = await controller.GetTenantUsage("tenant-alpha", null, CancellationToken.None);

        // Assert: Batch query called once with combined identities, zero sequential N+1 loops
        result.Should().BeOfType<OkObjectResult>();
        aiMetricsMock.Verify(m => m.GetBatchSummaryAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        aiMetricsMock.Verify(m => m.GetSummaryAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AiMetricsLogger_GetBatchSummaryAsync_AggregatesAcrossMultipleIdentitiesInSingleCall()
    {
        // Arrange
        var logger = new AiMetricsLogger(NullLogger<AiMetricsLogger>.Instance);
        var now = DateTime.UtcNow;

        logger.Record(new AiCallMetrics("gemini", "gemini-1.5-flash", "embed", 100, 20, 120, 0.001, TimeSpan.FromMilliseconds(50), false, false, "workload-1", now));
        logger.Record(new AiCallMetrics("gemini", "gemini-1.5-pro", "chat", 200, 50, 250, 0.005, TimeSpan.FromMilliseconds(120), false, false, "workload-2", now));
        logger.Record(new AiCallMetrics("openai", "gpt-4o", "chat", 300, 100, 400, 0.010, TimeSpan.FromMilliseconds(200), false, false, "tenant-alpha", now));
        logger.Record(new AiCallMetrics("openai", "gpt-4o", "chat", 999, 999, 1998, 0.050, TimeSpan.FromMilliseconds(200), false, false, "unrelated-tenant", now));

        // Act: Batch query for workloads + tenant in 1 pass
        var summary = await logger.GetBatchSummaryAsync(new[] { "workload-1", "workload-2", "tenant-alpha" }, now.AddMinutes(-5), CancellationToken.None);

        // Assert
        summary.CallCount.Should().Be(3);
        summary.TotalPromptTokens.Should().Be(600);
        summary.TotalCompletionTokens.Should().Be(170);
        summary.TotalTokens.Should().Be(770);
        summary.TotalEstimatedCostUsd.Should().BeApproximately(0.016, 0.0001);
    }

    // =========================================================================
    // 3. Missing Cancellation & Timeouts Tests
    // =========================================================================

    [Fact]
    public async Task MemoriesController_AddVisualMemory_WhenTokenCanceled_PropagatesOperationCanceledException()
    {
        // Arrange
        var mediatorMock = new Mock<IMediator>();
        var embeddingMock = new Mock<IEmbeddingService>();
        var vectorMock = new Mock<IVectorMemoryRepository>();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Token already canceled

        embeddingMock.Setup(e => e.GenerateVisualEmbeddingAsync(It.IsAny<VisualContent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var controller = new MemoriesController(
            mediatorMock.Object,
            embeddingMock.Object,
            vectorMock.Object,
            NullLogger<MemoriesController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Items["UserId"] = "user-123";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var dto = new CreateVisualMemoryDto(
            ImageBase64: Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            MimeType: "image/png",
            Description: "Canceled capture"
        );

        // Act & Assert: Must propagate OperationCanceledException instead of swallowing it
        var act = () => controller.AddVisualMemory(dto, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PurgeUserMemories_WhenCancellationRequested_ImmediatelyStopsAndThrows()
    {
        // Arrange
        var readRepoMock = new Mock<IReadModelRepository>();
        var eventStoreMock = new Mock<IEventStore>();
        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.IsAdmin).Returns(true);

        readRepoMock.Setup(r => r.GetMemoriesByUserAsync("user-target", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemoryReadModel>
            {
                new() { MemoryId = Guid.NewGuid(), UserId = "user-target" },
                new() { MemoryId = Guid.NewGuid(), UserId = "user-target" }
            });

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Token pre-canceled

        var handler = new PurgeUserMemoriesCommandHandler(
            readRepoMock.Object,
            Mock.Of<IRetentionQueryStore>(),
            eventStoreMock.Object,
            Mock.Of<IEventBus>(),
            Mock.Of<IVectorMemoryRepository>(),
            userContextMock.Object,
            NullLogger<PurgeUserMemoriesCommandHandler>.Instance);

        var command = new PurgeUserMemoriesCommand("user-target", "GDPR", "auditor-1");

        // Act & Assert: Must rethrow OperationCanceledException and avoid leaked work
        var act = () => handler.Handle(command, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        eventStoreMock.Verify(e => e.GetEventsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================
    // 4. Swallowed Exceptions Tests
    // =========================================================================

    [Fact]
    public async Task MemoriesController_AddVisualMemory_WhenEmbeddingThrowsGenericException_LogsWarningAndFallsBackGracefully()
    {
        // Arrange
        var mediatorMock = new Mock<IMediator>();
        var embeddingMock = new Mock<IEmbeddingService>();
        var vectorMock = new Mock<IVectorMemoryRepository>();
        var loggerMock = new Mock<ILogger<MemoriesController>>();

        var memoryId = Guid.NewGuid();
        mediatorMock.Setup(m => m.Send(It.IsAny<AddMemoryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(memoryId);

        embeddingMock.Setup(e => e.GenerateVisualEmbeddingAsync(It.IsAny<VisualContent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Embedding endpoint timeout 504"));

        var controller = new MemoriesController(
            mediatorMock.Object,
            embeddingMock.Object,
            vectorMock.Object,
            loggerMock.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Items["UserId"] = "user-123";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var dto = new CreateVisualMemoryDto(
            ImageBase64: Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            MimeType: "image/png",
            Description: "Fallback capture"
        );

        // Act: Non-cancellation exception should be logged as warning, textual memory saved
        var result = await controller.AddVisualMemory(dto, CancellationToken.None);

        // Assert
        result.Should().BeOfType<CreatedAtActionResult>();
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Falling back to background worker")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // =========================================================================
    // 5. Over-Exposed Data Entities Tests
    // =========================================================================

    [Fact]
    public async Task EmployeesController_GetAll_NonHrUser_SuppressesSalaryAndSsn()
    {
        // Arrange
        var repoMock = new Mock<IEmployeeRepository>();
        var authServiceMock = new Mock<IAuthorizationService>();

        var records = new List<EmployeeRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                UserId = "emp-1",
                TenantId = "tenant-corp",
                FullName = "Alice Smith",
                Department = "Engineering",
                Salary = 150000,
                SocialSecurityNumber = "123-45-6789"
            },
            new()
            {
                Id = Guid.NewGuid(),
                UserId = "emp-2",
                TenantId = "tenant-corp",
                FullName = "Bob Jones",
                Department = "Product",
                Salary = 130000,
                SocialSecurityNumber = "987-65-4321"
            }
        };

        repoMock.Setup(r => r.GetByTenantAsync("tenant-corp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var controller = new EmployeesController(authServiceMock.Object, repoMock.Object);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "emp-viewer"),
            new Claim(ClaimTypes.Role, "StandardEmployee"),
            new Claim("tenant_id", "tenant-corp")
        }, "Bearer"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await controller.GetAll(CancellationToken.None);

        // Assert: Maps to DTO, suppressing salary & SSN
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = okResult.Value.Should().BeAssignableTo<IEnumerable<EmployeeResponseDto>>().Subject.ToList();

        dtos.Should().HaveCount(2);
        dtos.All(d => d.Salary == null).Should().BeTrue();
        dtos.All(d => d.SocialSecurityNumber == null).Should().BeTrue();

        // Verify JSON serialization completely omits salary and SSN keys
        var json = JsonSerializer.Serialize(dtos);
        json.Should().NotContain("salary", because: "unprivileged callers must never see salary keys over the wire");
        json.Should().NotContain("socialSecurityNumber", because: "unprivileged callers must never see SSN keys over the wire");
    }

    [Fact]
    public async Task EmployeesController_GetById_EmployeeOwner_MasksSsnAndRevealsOwnSalary()
    {
        // Arrange
        var repoMock = new Mock<IEmployeeRepository>();
        var authServiceMock = new Mock<IAuthorizationService>();

        var recordId = Guid.NewGuid();
        var record = new EmployeeRecord
        {
            Id = recordId,
            UserId = "emp-alice",
            TenantId = "tenant-corp",
            FullName = "Alice Smith",
            Department = "Engineering",
            Salary = 150000,
            SocialSecurityNumber = "123-45-6789"
        };

        repoMock.Setup(r => r.GetByIdAsync(recordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        authServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), record, It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
            .ReturnsAsync(AuthorizationResult.Success());

        var controller = new EmployeesController(authServiceMock.Object, repoMock.Object);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "emp-alice"),
            new Claim(ClaimTypes.Role, "StandardEmployee"),
            new Claim("tenant_id", "tenant-corp")
        }, "Bearer"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await controller.GetById(recordId, CancellationToken.None);

        // Assert: Self view shows own salary, masks SSN to last 4 digits
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<EmployeeResponseDto>().Subject;

        dto.Salary.Should().Be(150000);
        dto.SocialSecurityNumber.Should().Be("***-**-6789");
    }

    [Fact]
    public async Task EmployeesController_GetById_AdminUser_ExposesFullSalaryAndUnmaskedSsn()
    {
        // Arrange
        var repoMock = new Mock<IEmployeeRepository>();
        var authServiceMock = new Mock<IAuthorizationService>();

        var recordId = Guid.NewGuid();
        var record = new EmployeeRecord
        {
            Id = recordId,
            UserId = "emp-alice",
            TenantId = "tenant-corp",
            FullName = "Alice Smith",
            Department = "Engineering",
            Salary = 150000,
            SocialSecurityNumber = "123-45-6789"
        };

        repoMock.Setup(r => r.GetByIdAsync(recordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        authServiceMock.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), record, It.IsAny<IEnumerable<IAuthorizationRequirement>>()))
            .ReturnsAsync(AuthorizationResult.Success());

        var controller = new EmployeesController(authServiceMock.Object, repoMock.Object);

        var adminUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin-hr"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("tenant_id", "tenant-corp")
        }, "Bearer"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = adminUser }
        };

        // Act
        var result = await controller.GetById(recordId, CancellationToken.None);

        // Assert: Admin view reveals unmasked SSN and salary
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<EmployeeResponseDto>().Subject;

        dto.Salary.Should().Be(150000);
        dto.SocialSecurityNumber.Should().Be("123-45-6789");
    }

    [Fact]
    public async Task AccessAssignments_GetStatus_ReturnsCleanDtoWithoutInternalOrchestrationFields()
    {
        // Arrange
        var assignmentId = Guid.NewGuid();
        var repoMock = new Mock<IAccessAssignmentRepository>();
        var userContextMock = new Mock<IUserContext>();
        userContextMock.Setup(u => u.UserId).Returns("user-alice");
        userContextMock.Setup(u => u.IsAdmin).Returns(false);
        userContextMock.Setup(u => u.Roles).Returns(new List<string> { "StandardUser" });

        var entity = new AccessAssignmentEntity
        {
            Id = assignmentId,
            UserId = "user-alice",
            TenantId = "tenant-1",
            TargetResourceId = "vault-42",
            PermissionLevel = "Read",
            Status = AssignmentStatus.Active,
            IdempotencyKey = "grant:secret-key-12345",
            RetryCount = 3,
            LastError = "Internal DB deadlocked during attempt 2",
            CreatedAtUtc = DateTime.UtcNow,
            ProvisionedAtUtc = DateTime.UtcNow
        };

        repoMock.Setup(r => r.GetByIdAsync(assignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var controller = new AccessAssignmentsController(
            repoMock.Object,
            userContextMock.Object,
            NullLogger<AccessAssignmentsController>.Instance);

        // Act
        var result = await controller.GetStatus(assignmentId, CancellationToken.None);

        // Assert: Returns AccessAssignmentResponseDto, internal orchestration fields are stripped
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<AccessAssignmentResponseDto>().Subject;

        dto.Id.Should().Be(assignmentId);
        dto.UserId.Should().Be("user-alice");
        dto.Status.Should().Be("Active");

        var json = JsonSerializer.Serialize(dto);
        json.Should().NotContain("IdempotencyKey");
        json.Should().NotContain("RetryCount");
        json.Should().NotContain("LastError");
    }
}
