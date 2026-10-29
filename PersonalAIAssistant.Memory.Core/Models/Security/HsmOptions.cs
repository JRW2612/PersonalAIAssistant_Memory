namespace PersonalAIAssistant.Memory.Core.Models.Security
{
    /// <summary>
    /// Configuration options for Cloud HSM (Azure Key Vault Managed HSM / AWS KMS HSM).
    /// Enforces FIPS 140-2 Level 3 hardware security module compliance.
    /// </summary>
    public sealed class HsmOptions
    {
        public const string SectionName = "Hsm";

        /// <summary>
        /// HSM Provider type: "AzureKeyVaultManagedHsm", "AwsKmsHsm", or "SimulatedHsm".
        /// </summary>
        public string Provider { get; set; } = "SimulatedHsm";

        /// <summary>
        /// Azure Key Vault / Managed HSM URI (e.g. https://my-bank-hsm.managedhsm.azure.net/).
        /// </summary>
        public string? KeyVaultUri { get; set; }

        /// <summary>
        /// AWS Region for KMS / CloudHSM (e.g. "ap-south-1").
        /// </summary>
        public string KmsRegion { get; set; } = "ap-south-1";

        /// <summary>
        /// AWS KMS Key ID or ARN backed by CloudHSM.
        /// </summary>
        public string? KmsKeyId { get; set; }

        /// <summary>
        /// Name of the JWT signing secret stored inside the HSM / Key Vault.
        /// </summary>
        public string JwtSecretKeyName { get; set; } = "JwtSigningSecret";

        /// <summary>
        /// Master Key (KEK) identifier used for envelope encryption of memory payloads.
        /// </summary>
        public string MasterKeyName { get; set; } = "MemoryMasterKey";

        /// <summary>
        /// Mandatory FIPS compliance level.
        /// </summary>
        public string FipsComplianceLevel { get; set; } = "FIPS 140-2 Level 3";

        /// <summary>
        /// Enforce that hardware keys must reside on HSM partition and cannot be exported.
        /// </summary>
        public bool EnforceHardwareSecurityModule { get; set; } = true;

        /// <summary>
        /// Automatic cryptographic zeroization of plaintext keys in RAM.
        /// </summary>
        public bool AutoZeroizeMemory { get; set; } = true;
    }
}
