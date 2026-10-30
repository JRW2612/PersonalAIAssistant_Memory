using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using PersonalAIAssistant.Memory.Api.Extensions;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Security;

/// <summary>
/// Verifies that ProductionConfigurationValidator throws InvalidOperationException
/// for every category of insecure or missing configuration when running in Production.
/// These tests are the machine-readable evidence that the fail-fast startup guard works.
/// </summary>
public class ProductionStartupValidationTests
{
    private static IHostEnvironment ProductionEnv() =>
        new HostingEnvironment { EnvironmentName = Environments.Production };

    private static IHostEnvironment DevelopmentEnv() =>
        new HostingEnvironment { EnvironmentName = Environments.Development };

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static Dictionary<string, string?> ValidProductionSettings() => new()
    {
        ["UseInMemoryStore"] = "false",
        ["Cors:AllowedOrigins:0"] = "https://app.bank.internal",
        ["Jwt:SecretKey"] = "ValidProductionJwtKey-AtLeast-32-Characters-Long-Secret!",
        ["Encryption:SystemKey"] = "ValidProductionEncKey-AtLeast-32-Characters-Long!",
        ["ConnectionStrings:PostgresReadModel"] = "Host=prod-pg;Database=BfsiDb;Username=app;Password=SecureP@ssw0rd;SSL Mode=Require",
        ["ConnectionStrings:MongoEventStore"] = "mongodb://app:SecureP@ssw0rd@prod-mongo:27017/BfsiDb?ssl=true"
    };

    // ─── PASS: Valid Production Config ────────────────────────────────────────

    [Fact]
    public void ValidProductionConfig_DoesNotThrow()
    {
        var config = BuildConfig(ValidProductionSettings());
        var env = ProductionEnv();

        // Must not throw
        config.ValidateProductionConfiguration(env);
    }

    [Fact]
    public void DevelopmentEnvironment_AlwaysPasses_RegardlessOfConfig()
    {
        // In Development, no validation is enforced — must not throw even with missing keys
        var config = BuildConfig(new Dictionary<string, string?> { ["UseInMemoryStore"] = "true" });
        config.ValidateProductionConfiguration(DevelopmentEnv()); // No exception
    }

    // ─── BLOCKER 1: InMemoryStore in Production ────────────────────────────────

    [Fact]
    public void Production_UseInMemoryStore_True_Throws()
    {
        var values = ValidProductionSettings();
        values["UseInMemoryStore"] = "true";
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("UseInMemoryStore", ex.Message);
        Assert.Contains("InMemoryStore", ex.Message);
    }

    [Fact]
    public void Production_UseInMemoryStore_Missing_Throws()
    {
        var values = ValidProductionSettings();
        values.Remove("UseInMemoryStore");
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("UseInMemoryStore", ex.Message);
    }

    // ─── BLOCKER 2: CORS must be explicit ─────────────────────────────────────

    [Fact]
    public void Production_NoCorsOrigins_Throws()
    {
        var values = ValidProductionSettings();
        values.Remove("Cors:AllowedOrigins:0");
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("CORS", ex.Message);
    }

    [Fact]
    public void Production_WildcardCorsOrigin_Throws()
    {
        var values = ValidProductionSettings();
        values["Cors:AllowedOrigins:0"] = "*";
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("CORS", ex.Message);
    }

    // ─── BLOCKER 3: JWT key must not be missing, empty, or a placeholder ──────

    [Fact]
    public void Production_MissingJwtKey_Throws()
    {
        var values = ValidProductionSettings();
        values.Remove("Jwt:SecretKey");
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("Jwt:SecretKey", ex.Message);
    }

    [Fact]
    public void Production_JwtKey_WithShellPlaceholder_Throws()
    {
        var values = ValidProductionSettings();
        values["Jwt:SecretKey"] = "${JWT_SIGNING_SECRET_FROM_HSM}";
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("Jwt:SecretKey", ex.Message);
        Assert.Contains("placeholder", ex.Message);
    }

    [Fact]
    public void Production_JwtKey_TooShort_Throws()
    {
        var values = ValidProductionSettings();
        values["Jwt:SecretKey"] = "tooshort";
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("Jwt:SecretKey", ex.Message);
    }

    [Fact]
    public void Production_JwtKey_WithKnownDevDefault_Throws()
    {
        var values = ValidProductionSettings();
        values["Jwt:SecretKey"] = "SuperSecretJwtAuthenticationSigningKey32BytesLongStringForHmacSha256!";
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("Jwt:SecretKey", ex.Message);
        Assert.Contains("forbidden", ex.Message);
    }

    // ─── BLOCKER 4: Encryption key must not be a placeholder or dev default ───

    [Fact]
    public void Production_EncryptionKey_WithShellPlaceholder_Throws()
    {
        var values = ValidProductionSettings();
        values["Encryption:SystemKey"] = "${MASTER_ENCRYPTION_KEY_FROM_HSM}";
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("Encryption:SystemKey", ex.Message);
        Assert.Contains("placeholder", ex.Message);
    }

    [Fact]
    public void Production_EncryptionKey_WithKnownDevDefault_Throws()
    {
        var values = ValidProductionSettings();
        values["Encryption:SystemKey"] = "ProductionSystemEncryptionKey32BytesLongSecretString!";
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("Encryption:SystemKey", ex.Message);
    }

    // ─── BLOCKER 5: Connection strings must not have placeholders or localhost──

    [Fact]
    public void Production_PostgresConnectionString_WithPlaceholder_Throws()
    {
        var values = ValidProductionSettings();
        values["ConnectionStrings:PostgresReadModel"] = "Host=prod-pg;Password=${POSTGRES_PROD_PASSWORD};Database=BfsiDb";
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("ConnectionStrings:PostgresReadModel", ex.Message);
        Assert.Contains("placeholder", ex.Message);
    }

    [Fact]
    public void Production_PostgresConnectionString_WithLocalhostDefault_Throws()
    {
        var values = ValidProductionSettings();
        values["ConnectionStrings:PostgresReadModel"] = "Host=localhost;Database=PersonalAiMemoryReadDb;Username=postgres;Password=postgres";
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("ConnectionStrings:PostgresReadModel", ex.Message);
    }

    [Fact]
    public void Production_Missing_PostgresConnectionString_Throws()
    {
        var values = ValidProductionSettings();
        values.Remove("ConnectionStrings:PostgresReadModel");
        var config = BuildConfig(values);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            config.ValidateProductionConfiguration(ProductionEnv()));

        Assert.Contains("ConnectionStrings:PostgresReadModel", ex.Message);
    }
}
