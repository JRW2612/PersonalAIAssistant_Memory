using Microsoft.AspNetCore.RateLimiting;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole();
builder.Services.AddHttpClient("upstream").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("provider", context =>
    {
        var partition = $"{context.Request.Headers["X-AI-User-Id"].FirstOrDefault() ?? "unknown"}:{context.Request.RouteValues["provider"]}";
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("Gateway:RequestsPerMinute", 60),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var app = builder.Build();
var internalToken = app.Configuration["Gateway:InternalAuthToken"]
    ?? throw new InvalidOperationException("Gateway:InternalAuthToken must be configured.");

app.Use(async (context, next) =>
{
    var supplied = context.Request.Headers["X-AI-Gateway-Key"].FirstOrDefault();
    var valid = !string.IsNullOrEmpty(supplied) &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(internalToken));
    if (!valid)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "invalid_gateway_credentials" });
        return;
    }
    await next();
});
app.UseRateLimiter();

app.MapPost("/v1/{provider}/{**path}", async (string provider, string path, HttpContext context, IHttpClientFactory clients, ILoggerFactory loggerFactory, CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("AiGatewayAudit");
    var route = ProviderRoute.TryCreate(provider, path, app.Configuration);
    if (route is null) return Results.NotFound(new { error = "provider_or_path_not_allowed" });

    if (context.Request.ContentLength is > 1_048_576) return Results.BadRequest(new { error = "payload_too_large" });
    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: false);
    var originalPayload = await reader.ReadToEndAsync(ct);
    var safePayload = SensitiveDataRedactor.RedactJson(originalPayload);
    var promptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalPayload)));
    var toolCount = CountRequestedTools(safePayload);
    var userId = context.Request.Headers["X-AI-User-Id"].FirstOrDefault() ?? "unknown";
    var model = context.Request.Headers["X-AI-Model"].FirstOrDefault() ?? "unspecified";
    var operation = context.Request.Headers["X-AI-Operation"].FirstOrDefault() ?? "generate";

    using var outbound = new HttpRequestMessage(HttpMethod.Post, route.Target)
    {
        Content = new StringContent(safePayload, Encoding.UTF8, context.Request.ContentType ?? "application/json")
    };
    route.ApplyCredentials(outbound);
    foreach (var header in context.Request.Headers.Where(h => h.Key is "Accept" or "X-Request-Id"))
        outbound.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());

    try
    {
        using var response = await clients.CreateClient("upstream").SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        // Store an output fingerprint rather than output content. This preserves forensic linkage
        // without copying potentially sensitive model output into the audit stream.
        var outputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        logger.LogInformation("AI gateway audit {@Audit}", new { userId, promptHash, provider, model, operation, status = (int)response.StatusCode, toolCalls = toolCount, reasoningSteps = 0, outputHash, outputLength = body.Length });
        return Results.Content(body, response.Content.Headers.ContentType?.ToString() ?? "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        logger.LogWarning("AI gateway audit {@Audit}", new { userId, promptHash, provider, model, operation, status = 502, toolCalls = toolCount, error = ex.GetType().Name });
        return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "AI provider request failed");
    }
}).RequireRateLimiting("provider");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

static int CountRequestedTools(string payload)
{
    try
    {
        return JsonNode.Parse(payload)?["tools"] is JsonArray tools ? tools.Count : 0;
    }
    catch (JsonException)
    {
        return 0;
    }
}

app.Run();

sealed class ProviderRoute
{
    private ProviderRoute(Uri target, string? authorization, string? apiKeyHeader, string? apiKey)
    {
        Target = target; Authorization = authorization; ApiKeyHeader = apiKeyHeader; ApiKey = apiKey;
    }
    public Uri Target { get; }
    private string? Authorization { get; }
    private string? ApiKeyHeader { get; }
    private string? ApiKey { get; }

    public static ProviderRoute? TryCreate(string provider, string path, IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal) || !Regex.IsMatch(path, "^[A-Za-z0-9_./:-]+$")) return null;
        var section = provider.ToLowerInvariant() switch
        {
            "openai" => "Upstreams:OpenAi", "google" => "Upstreams:Google", "anthropic" => "Upstreams:Anthropic", "local" => "Upstreams:Local", _ => null
        };
        if (section is null) return null;
        var baseUrl = config[$"{section}:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || !baseUri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        var target = new Uri(baseUri, path);
        var apiKey = config[$"{section}:ApiKey"];
        var keyHeader = config[$"{section}:ApiKeyHeader"];
        return new ProviderRoute(target, config[$"{section}:Authorization"], keyHeader, apiKey);
    }

    public void ApplyCredentials(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(Authorization)) request.Headers.TryAddWithoutValidation("Authorization", Authorization);
        if (!string.IsNullOrWhiteSpace(ApiKeyHeader) && !string.IsNullOrWhiteSpace(ApiKey)) request.Headers.TryAddWithoutValidation(ApiKeyHeader, ApiKey);
    }
}

static class SensitiveDataRedactor
{
    private static readonly Regex[] Patterns =
    [
        new(@"\b(?:sk-[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9]{36}|AIzaSy[A-Za-z0-9_-]{20,})\b", RegexOptions.Compiled),
        new(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b", RegexOptions.Compiled),
        new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled),
        new(@"\b(?:4\d{12}(?:\d{3})?|5[1-5]\d{14}|3[47]\d{13})\b", RegexOptions.Compiled),
        new(@"(?i)(?:password|passwd|pwd|secret)\s*[=:]\s*[^\s\""']+", RegexOptions.Compiled)
    ];

    public static string RedactJson(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return payload;
        JsonNode? node;
        try { node = JsonNode.Parse(payload); } catch (JsonException) { return "{\"error\":\"invalid_json\"}"; }
        Redact(node);
        return node?.ToJsonString() ?? "null";
    }

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToList())
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text)) obj[pair.Key] = RedactText(text); else Redact(pair.Value);
        else if (node is JsonArray array)
            for (var i = 0; i < array.Count; i++)
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = RedactText(text); else Redact(array[i]);
    }

    private static string RedactText(string text) => Patterns.Aggregate(text, (current, pattern) => pattern.Replace(current, "[REDACTED]"));
}
