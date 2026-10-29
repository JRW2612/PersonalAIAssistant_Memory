using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models.Security;

namespace PersonalAIAssistant.Memory.Api.Controllers
{
    [ApiController]
    [Route("api/v1/ui")]
    [Authorize]
    public class UiNavigationController : ControllerBase
    {
        private readonly IUiCapabilitiesService _capabilitiesService;
        private readonly ISecurityAuditLogger _auditLogger;

        public UiNavigationController(
            IUiCapabilitiesService capabilitiesService,
            ISecurityAuditLogger auditLogger)
        {
            _capabilitiesService = capabilitiesService;
            _auditLogger = auditLogger;
        }

        /// <summary>
        /// Authoritatively computes the UI capabilities, feature flags, and visible actions
        /// for the authenticated user based strictly on cryptographic token claims and tenant boundary.
        /// Frontends consume this for rendering UX; backend remains the sole authority.
        /// </summary>
        [HttpGet("capabilities")]
        [ProducesResponseType(typeof(UiCapabilitiesDescriptor), StatusCodes.Status200OK)]
        public IActionResult GetCapabilities()
        {
            var capabilities = _capabilitiesService.BuildCapabilities(User);
            return Ok(capabilities);
        }

        /// <summary>
        /// Returns the role- and tenant-filtered navigation hierarchy for the authenticated caller.
        /// </summary>
        [HttpGet("navigation")]
        [ProducesResponseType(typeof(IReadOnlyList<NavigationItem>), StatusCodes.Status200OK)]
        public IActionResult GetNavigation()
        {
            var capabilities = _capabilitiesService.BuildCapabilities(User);
            return Ok(capabilities.VisibleNavigation);
        }

        public record TamperSimulationRequest(string TargetAction, string? TargetResourceId = null, string? TargetTenantId = null);

        /// <summary>
        /// Security demonstration endpoint: Simulates a client-side attacker tampering with DOM/JavaScript
        /// to trigger an administrative or cross-tenant action.
        /// Proves that the backend authoritatively rejects the request with HTTP 403 Forbidden and audits the violation.
        /// </summary>
        [HttpPost("simulate-tamper")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public IActionResult SimulateTamperAttempt([FromBody] TamperSimulationRequest request)
        {
            var capabilities = _capabilitiesService.BuildCapabilities(User);
            var callerUserId = capabilities.UserId;
            var callerTenantId = capabilities.TenantId;

            // Check if user is attempting cross-tenant manipulation
            if (!string.IsNullOrEmpty(request.TargetTenantId) &&
                !string.Equals(request.TargetTenantId, callerTenantId, StringComparison.OrdinalIgnoreCase))
            {
                _auditLogger.LogCrossTenantAttempt(
                    callerUserId,
                    callerTenantId,
                    request.TargetResourceId ?? "target-tamper-resource",
                    request.TargetTenantId,
                    request.TargetAction);

                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    error = "Security Violation: Cross-tenant access blocked by backend authority.",
                    action = request.TargetAction,
                    callerTenant = callerTenantId,
                    targetTenant = request.TargetTenantId,
                    tamperDetected = true
                });
            }

            // Check if user is attempting privileged action without backend permission
            if (capabilities.ActionCapabilities.TryGetValue(request.TargetAction, out var cap))
            {
                if (!cap.IsEnabled)
                {
                    _auditLogger.LogAccessDenied(
                        callerUserId,
                        request.TargetAction,
                        request.TargetResourceId ?? "action",
                        cap.RestrictionReason ?? "Insufficient privileges");

                    return StatusCode(StatusCodes.Status403Forbidden, new
                    {
                        error = $"Security Violation: Action '{request.TargetAction}' rejected by backend authority.",
                        reason = cap.RestrictionReason,
                        tamperDetected = true
                    });
                }
            }

            return Ok(new
            {
                message = $"Action '{request.TargetAction}' is authorized for your role and tenant.",
                authorized = true
            });
        }
    }
}
