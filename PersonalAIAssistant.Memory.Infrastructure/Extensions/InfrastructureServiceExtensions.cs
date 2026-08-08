using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using PersonalAIAssistant.Memory.Core.Interfaces.Mongo;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Interfaces.Sql;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.AI;
using PersonalAIAssistant.Memory.Infrastructure.AI.Gemini;
using PersonalAIAssistant.Memory.Infrastructure.AI.OpenAi;
using PersonalAIAssistant.Memory.Infrastructure.AI.Teams;
using PersonalAIAssistant.Memory.Infrastructure.EF;
using PersonalAIAssistant.Memory.Infrastructure.InMemory;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;

namespace PersonalAIAssistant.Memory.Infrastructure.Extensions
{
    public static class InfrastructureServiceExtensions
    {
        public static IServiceCollection AddMemoryInfrastructureServices(
            this IServiceCollection services,
            Action<DbContextOptionsBuilder> configureDbContext,
            string mongoConnectionString,
            string mongoDatabaseName = "PersonalAIAssistantMemory")
        {
            // Register EF Core Read Model DbContext & Repository
            services.AddDbContext<ReadModelDbContext>(configureDbContext);
            services.AddScoped<IReadModelRepository, SqlReadModelRepository>();
            services.AddScoped<ITransactionalReadModelRepository>(sp =>
                (SqlReadModelRepository)sp.GetRequiredService<IReadModelRepository>());

            // Register MongoDB client, Database & Event Store / Snapshot Repository
            services.AddSingleton<IMongoClient>(new MongoClient(mongoConnectionString));
            services.AddSingleton<IMongoDatabase>(sp =>
            {
                var client = sp.GetRequiredService<IMongoClient>();
                return client.GetDatabase(mongoDatabaseName);
            });
            services.AddSingleton<IEventStore, MongoEventStore>();
            services.AddSingleton<ISnapshotRepository, MongoSnapshotRepository>();

            // Register Event Bus (InMemory implementation with scoped handler dispatch)
            services.AddSingleton<IEventBus, InMemoryEventBus>();

            return services;
        }

        // ─────────────────────────────────────────────────────────────────────
        // AI Providers + Teams Webhook
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Registers OpenAI, Gemini, and Teams Webhook services.
        /// Call this alongside AddMemoryInfrastructureServices() in the host builder.
        /// </summary>
        public static IServiceCollection AddAiProviders(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // ── Bind strongly-typed options ──────────────────────────────────
            services.Configure<AiProviderOptions>(configuration.GetSection(AiProviderOptions.SectionName));
            services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
            services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
            services.Configure<TeamsOptions>(configuration.GetSection(TeamsOptions.SectionName));
            
            services.Configure<ChunkingOptions>(configuration.GetSection("Chunking"));
            services.Configure<RetentionOptions>(configuration.GetSection(RetentionOptions.SectionName));

            // ── Named HttpClients ────────────────────────────────────────────
            services.AddHttpClient("openai", client =>
            {
                client.BaseAddress = new Uri("https://api.openai.com/v1/");
                client.Timeout     = TimeSpan.FromSeconds(60);
            });

            services.AddHttpClient("gemini", client =>
            {
                client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
                client.Timeout     = TimeSpan.FromSeconds(60);
            });

            services.AddHttpClient("teams", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // ── Provider implementations ─────────────────────────────────────
            // All registered so IEnumerable<IAIProvider> resolves all of them;
            // AIProviderFactory picks the right one by name.
            services.AddScoped<IAIProvider, OpenAiChatProvider>();
            services.AddScoped<IAIProvider, GeminiChatProvider>();
            services.AddScoped<IAIProviderFactory, AIProviderFactory>();

            // Teams notification sender
            services.AddScoped<INotificationSender, TeamsWebhookSender>();

            // ── New memory features (Chunking, Metrics, Retrieval) ───────────
            services.AddSingleton<IAiMetricsLogger, AiMetricsLogger>();
            services.AddSingleton<ITextChunker, TextChunker>();
            services.AddScoped<IMemoryRetrievalService, MemoryRetrievalService>();

            return services;
        }
    }
}
