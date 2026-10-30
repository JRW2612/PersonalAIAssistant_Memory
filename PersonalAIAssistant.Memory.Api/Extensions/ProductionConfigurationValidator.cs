using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PersonalAIAssistant.Memory.Api.Extensions;

/// <summary>
/// Enforces fail-fast validation for Production environments.
/// Prevents the application from booting with insecure defaults, unexpanded secret placeholders,
/// development credentials, or in-memory persistence in production deployments.
/// </summary>
public static class ProductionConfigurationValidator
{
    private static readonly string[] ProhibitedDevSecretPatterns =
    {
        "SuperSecretJwtAuthenticationSigningKey",
        "ProductionSystemEncryptionKey32BytesLongSecretString",
        "DevelopmentSystemEncryptionKey",
        "EnterpriseBfsiFipsHsmJwtSigningKey",
        "postgres:postgres",
        "guest:guest",
        "localhost"
    };

    public static void ValidateProductionConfiguration(this IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsProduction())
        {
            return;
        }

        // 1. Storage Backend Validation (Fail-Fast: InMemory is prohibited in Production)
        var inMemoryValue = configuration.GetValue<bool?>("UseInMemoryStore");
        if (inMemoryValue != false)
        {
            throw new InvalidOperationException(
                "CRITICAL CONFIGURATION ERROR: Production deployment cannot use InMemoryStore ('UseInMemoryStore' must be explicitly set to 'false'). Provide durable PostgreSQL and Qdrant cluster configurations.");
        }

        // 2. CORS Policy Validation (Fail-Fast: Wildcard or fallback-open is prohibited in Production)
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        if (allowedOrigins.Length == 0 || allowedOrigins.Any(o => string.IsNullOrWhiteSpace(o) || o.Trim() == "*"))
        {
            throw new InvalidOperationException(
                "CRITICAL CONFIGURATION ERROR: Production CORS policy requires explicit, non-wildcard origins configured in 'Cors:AllowedOrigins'. Wildcard '*' or fallback-open behavior is strictly prohibited in Production.");
        }

        // 3. Mandatory Production Secrets Validation
        ValidateSecret(configuration, "Jwt:SecretKey", minLength: 32, required: true);
        ValidateSecret(configuration, "Encryption:SystemKey", minLength: 32, required: true);
        ValidateSecret(configuration, "ConnectionStrings:PostgresReadModel", minLength: 10, required: true);
        ValidateSecret(configuration, "ConnectionStrings:MongoEventStore", minLength: 10, required: true);

        // 4. Validate Provider & Infrastructure Key Placeholders
        ValidateSecret(configuration, "Qdrant:ApiKey", minLength: 0, required: false);
        ValidateSecret(configuration, "AI:Gemini:ApiKey", minLength: 0, required: false);
        ValidateSecret(configuration, "AI:OpenAi:ApiKey", minLength: 0, required: false);
    }

    private static void ValidateSecret(IConfiguration configuration, string key, int minLength, bool required)
    {
        var value = configuration[key];

        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                throw new InvalidOperationException(
                    $"CRITICAL CONFIGURATION ERROR: Required production setting '{key}' is missing or empty. In production, provide this via environment variables or a configured secret provider (e.g. Azure Key Vault / AWS Secrets Manager).");
            }
            return;
        }

        // Check for unexpanded ${...} shell/compose placeholders
        if (value.Contains("${") && value.Contains("}"))
        {
            throw new InvalidOperationException(
                $"CRITICAL CONFIGURATION ERROR: Production setting '{key}' contains an unexpanded placeholder: '{value}'. .NET configuration does not expand shell tokens automatically. Configure production secrets via environment variables (e.g. {key.Replace(':', '_').Replace('.', '_')}) or an external Key Vault.");
        }

        // Check for minimum length
        if (minLength > 0 && value.Trim().Length < minLength)
        {
            throw new InvalidOperationException(
                $"CRITICAL CONFIGURATION ERROR: Production setting '{key}' does not meet minimum entropy requirements (must be at least {minLength} characters).");
        }

        // Check against known development defaults and weak credentials
        foreach (var pattern in ProhibitedDevSecretPatterns)
        {
            if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"CRITICAL CONFIGURATION ERROR: Production setting '{key}' contains forbidden development/insecure credential pattern '{pattern}'. Production credentials must be rotated and injected from enterprise secret storage.");
            }
        }
    }
}
