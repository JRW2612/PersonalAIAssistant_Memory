using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using PersonalAIAssistant.Memory.Api.DTOs;
using PersonalAIAssistant.Memory.Core.Entities;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;

namespace PersonalAIAssistant.Memory.Api.Controllers
{
    [ApiController]
    [Route("api/v1/workloads")]
    public class WorkloadsController : ControllerBase
    {
        private readonly IWorkloadIdentityRepository _repo;
        private readonly IWorkloadAuthenticationService _authService;
        private readonly IProviderTokenService _tokenService;
        private readonly IUserContext _userContext;
        private readonly ISecurityAuditLogger _auditLogger;
        private readonly IConfiguration _config;
        private readonly ILogger<WorkloadsController> _logger;

        public WorkloadsController(
            IWorkloadIdentityRepository repo,
            IWorkloadAuthenticationService authService,
            IProviderTokenService tokenService,
            IUserContext userContext,
            ISecurityAuditLogger auditLogger,
            IConfiguration config,
            ILogger<WorkloadsController> logger)
        {
            _repo = repo;
            _authService = authService;
            _tokenService = tokenService;
            _userContext = userContext;
            _auditLogger = auditLogger;
            _config = config;
            _logger = logger;
        }

        private string ResolveTenantId()
        {
            return !string.IsNullOrWhiteSpace(_userContext.TenantId) ? _userContext.TenantId : "default";
        }

        /// <summary>
        /// M2M OAuth 2.0 Token Endpoint: Exchanges cloud workload credentials (Client Credentials or OIDC Token)
        /// for a scoped, signed Machine-to-Machine JWT session token.
        /// </summary>
        [HttpPost("token")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(M2MTokenResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> IssueM2MToken(
            [FromBody] M2MTokenRequestDto request,
            CancellationToken ct)
        {
            WorkloadIdentityEntity? workload = null;

            if (!string.IsNullOrWhiteSpace(request.OidcToken))
            {
                // Authenticate via OIDC Workload Identity Federation (GitHub Actions, AWS IAM, Azure Managed ID, GCP SA)
                workload = await _authService.AuthenticateOidcTokenAsync(request.OidcToken, ct);
            }
            else if (!string.IsNullOrWhiteSpace(request.ClientId) && !string.IsNullOrWhiteSpace(request.ClientSecret))
            {
                // Authenticate via OAuth 2.0 Client Credentials
                workload = await _authService.AuthenticateClientCredentialsAsync(request.ClientId, request.ClientSecret, ct);
            }

            if (workload == null)
            {
                return Unauthorized(new { error = "invalid_client", error_description = "Invalid or unverified cloud workload credentials." });
            }

            // Issue M2M scoped JWT
            var jwtSecret = _config["Jwt:SecretKey"] ?? "SuperSecretJwtAuthenticationSigningKey32BytesLongStringForHmacSha256!";
            var jwtIssuer = _config["Jwt:Issuer"] ?? "PersonalAIAssistant.Memory";
            var jwtAudience = _config["Jwt:Audience"] ?? "PersonalAIAssistant.Memory.Api";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var expiresInSeconds = 3600; // 1 hour for M2M workloads
            var expires = DateTime.UtcNow.AddSeconds(expiresInSeconds);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, $"service:{workload.ClientId}"),
                new("client_id", workload.ClientId),
                new("workload_id", workload.ClientId),
                new("cloud_platform", workload.CloudPlatform),
                new("tenant_id", workload.TenantId),
                new(ClaimTypes.Role, "ServicePrincipal"),
                new("scope", workload.AllowedScopes)
            };

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            _logger.LogInformation("[M2M Token] Issued token for cloud workload '{ClientId}' ({Platform}) on tenant '{Tenant}'.",
                workload.ClientId, workload.CloudPlatform, workload.TenantId);

            return Ok(new M2MTokenResponseDto(
                AccessToken: tokenString,
                TokenType: "Bearer",
                ExpiresIn: expiresInSeconds,
                Scope: workload.AllowedScopes,
                ClientId: workload.ClientId,
                TenantId: workload.TenantId
            ));
        }

        /// <summary>
        /// Lists all registered cloud workloads for the current tenant.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<WorkloadIdentityResponseDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetWorkloads(CancellationToken ct)
        {
            var tenantId = ResolveTenantId();
            var workloads = await _repo.GetByTenantIdAsync(tenantId, ct);

            var dtos = workloads.Select(w => new WorkloadIdentityResponseDto(
                Id: w.Id,
                ClientId: w.ClientId,
                Name: w.Name,
                CloudPlatform: w.CloudPlatform,
                AuthType: w.AuthType.ToString(),
                TenantId: w.TenantId,
                AllowedScopes: w.AllowedScopes,
                Status: w.Status.ToString(),
                OidcIssuer: w.OidcIssuer,
                OidcAudience: w.OidcAudience,
                OidcSubjectFilter: w.OidcSubjectFilter,
                CreatedAtUtc: w.CreatedAtUtc,
                LastUsedAtUtc: w.LastUsedAtUtc
            )).ToList();

            return Ok(dtos);
        }

        /// <summary>
        /// Registers a new Cloud Service or M2M workload identity (AWS, Azure, GCP, GitHub).
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(WorkloadIdentityResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RegisterWorkload(
            [FromBody] RegisterWorkloadRequestDto request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("ClientId and Name are required.");
            }

            if (!Enum.TryParse<WorkloadAuthType>(request.AuthType, true, out var authType))
            {
                authType = WorkloadAuthType.OidcWorkloadIdentity;
            }

            var tenantId = !string.IsNullOrWhiteSpace(request.TenantId) ? request.TenantId : ResolveTenantId();

            string? hashedSecret = null;
            if (!string.IsNullOrWhiteSpace(request.ClientSecret))
            {
                var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(request.ClientSecret));
                hashedSecret = Convert.ToHexString(bytes).ToLowerInvariant();
            }

            string? encryptedWebhookSecret = null;
            if (!string.IsNullOrWhiteSpace(request.WebhookSecret))
            {
                encryptedWebhookSecret = _tokenService.EncryptToken(request.WebhookSecret);
            }

            var workload = new WorkloadIdentityEntity
            {
                ClientId = request.ClientId.Trim().ToLowerInvariant(),
                Name = request.Name.Trim(),
                CloudPlatform = !string.IsNullOrWhiteSpace(request.CloudPlatform) ? request.CloudPlatform.Trim() : "GenericM2M",
                AuthType = authType,
                TenantId = tenantId,
                AllowedScopes = !string.IsNullOrWhiteSpace(request.AllowedScopes)
                    ? request.AllowedScopes.Trim()
                    : "ai.memory.read ai.memory.write",
                HashedSecret = hashedSecret,
                OidcIssuer = request.OidcIssuer?.Trim(),
                OidcAudience = request.OidcAudience?.Trim(),
                OidcSubjectFilter = request.OidcSubjectFilter?.Trim(),
                EncryptedWebhookSecret = encryptedWebhookSecret,
                Status = WorkloadStatus.Active
            };

            await _repo.UpsertAsync(workload, ct);

            _auditLogger.LogAccessGranted(workload.ClientId, "REGISTER_WORKLOAD", workload.CloudPlatform);

            var dto = new WorkloadIdentityResponseDto(
                Id: workload.Id,
                ClientId: workload.ClientId,
                Name: workload.Name,
                CloudPlatform: workload.CloudPlatform,
                AuthType: workload.AuthType.ToString(),
                TenantId: workload.TenantId,
                AllowedScopes: workload.AllowedScopes,
                Status: workload.Status.ToString(),
                OidcIssuer: workload.OidcIssuer,
                OidcAudience: workload.OidcAudience,
                OidcSubjectFilter: workload.OidcSubjectFilter,
                CreatedAtUtc: workload.CreatedAtUtc,
                LastUsedAtUtc: workload.LastUsedAtUtc
            );

            return CreatedAtAction(nameof(GetWorkloads), dto);
        }

        /// <summary>
        /// Revokes access for a Cloud Workload identity.
        /// </summary>
        [HttpDelete("{clientId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> RevokeWorkload(
            string clientId,
            [FromBody] RevokeWorkloadRequestDto? request,
            CancellationToken ct)
        {
            var reason = request?.Reason ?? "Administratively revoked";
            await _repo.RevokeAsync(clientId, reason, ct);
            _auditLogger.LogAccessDenied(clientId, "REVOKE_WORKLOAD", clientId, reason);
            return NoContent();
        }
    }
}
