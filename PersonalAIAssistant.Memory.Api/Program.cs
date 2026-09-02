using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using PersonalAIAssistant.Memory.Api.Extensions;
using PersonalAIAssistant.Memory.Api.Middleware;
using PersonalAIAssistant.Memory.Business.Extensions;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Infrastructure.Extensions;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Observability (Serilog, OpenTelemetry, Health Checks)
builder.AddObservability();

// 2. Add API Controllers & OpenAPI/Swagger
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<PersonalAIAssistant.Memory.Core.Interfaces.Security.IUserContext, PersonalAIAssistant.Memory.Api.Security.HttpUserContext>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Personal AI Assistant — Memory Core API",
        Version = "v1",
        Description = "Enterprise CQRS & Event Sourced Memory Core with Vector Similarity Retrieval, Transparent Encryption, and Multi-Tenancy."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// 3. Configure JWT Authentication (SEC-02, SEC-06)
var jwtSecret = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("JWT signing key 'Jwt:SecretKey' is not configured. Set it via environment variables or user-secrets.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "PersonalAIAssistant.Memory";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "PersonalAIAssistant.Memory.Api";
var keyBytes = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ClockSkew = TimeSpan.FromMinutes(2)
    };
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 4. Register Infrastructure Services (EF Core PostgreSQL/InMemory, MongoDB/InMemory, Qdrant, MassTransit/RabbitMQ)
var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryStore", true);
var postgresConn = builder.Configuration.GetConnectionString("PostgresReadModel")
    ?? "Host=localhost;Database=PersonalAiMemoryReadDb;Username=postgres;Password=postgres";
var mongoConn = builder.Configuration.GetConnectionString("MongoEventStore")
    ?? "mongodb://localhost:27017";

builder.Services.AddMemoryInfrastructureServices(
    configureDbContext: options =>
    {
        if (useInMemory)
        {
            options.UseInMemoryDatabase("PersonalAiMemoryReadDb")
                   .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
        }
        else
        {
            options.UseNpgsql(postgresConn);
        }
    },
    mongoConnectionString: mongoConn,
    mongoDatabaseName: "PersonalAiMemoryDb"
);
builder.Services.AddAiProviders(builder.Configuration);

// Bind Outbox options (cleanup retention and interval)
builder.Services.Configure<PersonalAIAssistant.Memory.Infrastructure.Messaging.OutboxOptions>(builder.Configuration.GetSection("Outbox"));

if (useInMemory)
{
    builder.Services.AddSingleton<IEventStore, PersonalAIAssistant.Memory.Infrastructure.Mongo.InMemoryEventStore>();
    builder.Services.AddSingleton<ISnapshotRepository, PersonalAIAssistant.Memory.Infrastructure.Mongo.InMemorySnapshotRepository>();
    builder.Services.AddScoped<IEventBus, PersonalAIAssistant.Memory.Infrastructure.InMemory.InMemoryEventBus>();
    builder.Services.AddSingleton<IVectorMemoryRepository, PersonalAIAssistant.Memory.Infrastructure.InMemory.InMemoryVectorMemoryRepository>();
}
else
{
    // Use EF-based event store for production mode (transactional events + EF outbox)
    builder.Services.AddScoped<IEventStore, PersonalAIAssistant.Memory.Infrastructure.EF.EfEventStore>();
    // Register hosted service that dispatches EF outbox messages to MassTransit (RabbitMQ)
    builder.Services.AddHostedService<PersonalAIAssistant.Memory.Infrastructure.EF.EfOutboxDispatcherService>();
}

// 5. Register Business Layer Services & Pipeline Behaviors (Logging, Validation, Authorization)
builder.Services.AddMemoryBusinessServices(
    configureConsolidation: opts => { opts.BatchSize = 10; opts.PollInterval = TimeSpan.FromMinutes(2); },
    configureSnapshot: opts => { opts.BatchSize = 5; opts.PollInterval = TimeSpan.FromMinutes(5); }
);

var app = builder.Build();

// 6. Middleware Pipeline
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Memory Core API v1"));
}

app.UseRouting();
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseMiddleware<UserContextMiddleware>();
app.UseAuthorization();

app.UseObservabilityEndpoints();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapControllers();

// Outbox health endpoint: reports pending counts and last dispatched times for EF and Mongo outboxes
app.MapGet("/health/outbox", async (IServiceProvider sp) =>
{
    using var scope = sp.CreateScope();

    var efDb = scope.ServiceProvider.GetService(typeof(PersonalAIAssistant.Memory.Infrastructure.EF.EventStoreDbContext)) as PersonalAIAssistant.Memory.Infrastructure.EF.EventStoreDbContext;
    var mongoDb = scope.ServiceProvider.GetService(typeof(IMongoDatabase)) as IMongoDatabase;

    var result = new System.Collections.Generic.Dictionary<string, object?>();

    if (efDb != null)
    {
        try
        {
            var pending = await efDb.OutboxMessages.CountAsync(o => o.DispatchedAt == null);
            var lastDispatched = await efDb.OutboxMessages.Where(o => o.DispatchedAt != null).OrderByDescending(o => o.DispatchedAt).Select(o => o.DispatchedAt).FirstOrDefaultAsync();
            result["ef"] = new { pending, lastDispatched };
        }
        catch (Exception ex)
        {
            result["ef"] = new { error = ex.Message };
        }
    }

    if (mongoDb != null)
    {
        try
        {
            var coll = mongoDb.GetCollection<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>("outbox");
            var pending = await coll.CountDocumentsAsync(Builders<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>.Filter.Eq(d => d.DispatchedAt, null as DateTime?), cancellationToken: default);
            var last = await coll.Find(Builders<PersonalAIAssistant.Memory.Infrastructure.Mongo.OutboxDocument>.Filter.Ne(d => d.DispatchedAt, null as DateTime?))
                                .SortByDescending(d => d.DispatchedAt)
                                .Limit(1)
                                .Project(d => d.DispatchedAt)
                                .FirstOrDefaultAsync();
            result["mongo"] = new { pending, lastDispatched = last };
        }
        catch (Exception ex)
        {
            result["mongo"] = new { error = ex.Message };
        }
    }

    return Results.Json(result);
});

app.Run();
