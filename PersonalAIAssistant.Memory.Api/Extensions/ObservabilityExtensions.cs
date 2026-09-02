using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using System.Text.Json;

namespace PersonalAIAssistant.Memory.Api.Extensions
{
    public static class ObservabilityExtensions
    {
        public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
        {
            // 1. Serilog Setup
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "PersonalAIAssistant.Memory.Api")
                .WriteTo.Console()
                .CreateLogger();

            builder.Host.UseSerilog();

            // 2. OpenTelemetry Tracing & Metrics
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService("PersonalAIAssistant.Memory.Api"))
                .WithTracing(tracing =>
                {
                    tracing
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation();
                })
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation();
                });

            // 3. Health Checks
            builder.Services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy("Service is running"), tags: new[] { "live" });

            return builder;
        }

        public static IApplicationBuilder UseObservabilityEndpoints(this IApplicationBuilder app)
        {
            app.UseHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live"),
                ResponseWriter = WriteHealthResponse
            });

            app.UseHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = _ => true,
                ResponseWriter = WriteHealthResponse
            });

            return app;
        }

        private static async Task WriteHealthResponse(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json";
            var result = JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description
                })
            });
            await context.Response.WriteAsync(result);
        }
    }
}
