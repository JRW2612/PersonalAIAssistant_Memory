using System.Security.Claims;
using PersonalAIAssistant.Memory.Core.Models.Security;

namespace PersonalAIAssistant.Memory.Core.Interfaces.Security
{
    public interface IUiCapabilitiesService
    {
        /// <summary>
        /// Evaluates the authenticated caller's identity claims, tenant scope, roles, and granular permissions
        /// to build the authoritative UI visibility and capabilities descriptor.
        /// Frontends use this solely for UI/UX rendering; the backend independently re-enforces all authorization checks.
        /// </summary>
        UiCapabilitiesDescriptor BuildCapabilities(ClaimsPrincipal principal);
    }
}
