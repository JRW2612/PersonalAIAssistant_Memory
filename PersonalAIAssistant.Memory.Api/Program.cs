using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PersonalAIAssistant.Memory.Api.Middleware;
using PersonalAIAssistant.Memory.Api.Extensions;
using PersonalAIAssistant.Memory.Business.Extensions;
using PersonalAIAssistant.Memory.Infrastructure.Extensions;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Observability (Serilog, OpenTelemetry, Health Checks)
builder.AddObservability();

// 2. Add API Controllers & OpenAPI/Swagger
builder.Services.AddControllers();
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
var jwtSecret = builder.Configuration["Jwt:SecretKey"] ?? "SuperSecretJwtAuthenticationSigningKey32BytesLongStringForHmacSha256!";
var keyBytes = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.FromMinutes(5)
    };
});

builder.Services.AddAuthorization();

// 4. Register Infrastructure Services (EF Core PostgreSQL, MongoDB, Qdrant, MassTransit/RabbitMQ)
var postgresConn = builder.Configuration.GetConnectionString("PostgresReadModel") 
    ?? "Host=localhost;Database=PersonalAiMemoryReadDb;Username=postgres;Password=postgres";
var mongoConn = builder.Configuration.GetConnectionString("MongoEventStore") 
    ?? "mongodb://localhost:27017";

builder.Services.AddMemoryInfrastructureServices(
    configureDbContext: options => options.UseNpgsql(postgresConn),
    mongoConnectionString: mongoConn,
    mongoDatabaseName: "PersonalAiMemoryDb"
);

builder.Services.AddAiProviders(builder.Configuration);

// 5. Register Business Layer Services & Pipeline Behaviors (Logging, Validation, Authorization)
builder.Services.AddMemoryBusinessServices(
    configureConsolidation: opts => { opts.BatchSize = 10; opts.PollInterval = TimeSpan.FromMinutes(2); },
    configureSnapshot: opts => { opts.BatchSize = 5; opts.PollInterval = TimeSpan.FromMinutes(5); }
);

var app = builder.Build();

// 6. Middleware Pipeline
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Memory Core API v1"));
}

app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<UserContextMiddleware>();
app.UseAuthorization();

app.UseObservabilityEndpoints();

app.MapControllers();

app.Run();
