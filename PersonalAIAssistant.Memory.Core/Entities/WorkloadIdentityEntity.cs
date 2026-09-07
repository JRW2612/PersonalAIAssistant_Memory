namespace PersonalAIAssistant.Memory.Core.Entities
{
    public enum WorkloadAuthType
    {
        OidcWorkloadIdentity = 1,
        ClientCredentials = 2,
        HmacWebhook = 3
    }

    public enum WorkloadStatus
    {
        Active = 1,
        Suspended = 2,
        Revoked = 3
    }

    /// <summary>
    /// Represents an authorized Cloud Service (AWS, Azure, GCP, GitHub) or M2M automated workload.
    /// Supports zero-secret OIDC Workload Identity Federation, Client Credentials, and HMAC Webhook signatures.
    /// </summary>
    public sealed class WorkloadIdentityEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string ClientId { get; set; } = string.Empty; // Unique natural key, e.g. "github:myorg/repo", "aws:bedrock-agent"
        public string Name { get; set; } = string.Empty;
        public string CloudPlatform { get; set; } = "GenericM2M"; // "GitHub", "AWS", "Azure", "GCP", "GenericM2M"
        public WorkloadAuthType AuthType { get; set; } = WorkloadAuthType.OidcWorkloadIdentity;
        public string TenantId { get; set; } = "default";
        public string AllowedScopes { get; set; } = "ai.memory.read ai.memory.write";
        
        // For ClientCredentials M2M grant
        public string? HashedSecret { get; set; }
        
        // For OIDC Workload Identity Federation (GitHub Actions, AWS IAM, Azure Managed ID, GCP SA)
        public string? OidcIssuer { get; set; }
        public string? OidcAudience { get; set; }
        public string? OidcSubjectFilter { get; set; } // Supports wildcards, e.g. "repo:my-org/*" or ARN / SPN
        
        // For Webhook HMAC signature verification
        public string? EncryptedWebhookSecret { get; set; }

        public WorkloadStatus Status { get; set; } = WorkloadStatus.Active;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public string? RevocationReason { get; set; }
    }
}
