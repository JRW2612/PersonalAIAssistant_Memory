using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using PersonalAIAssistant.Memory.Core.Interfaces.EventSourcing;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Core.Interfaces.Persistence;
using PersonalAIAssistant.Memory.Core.Interfaces.AI;
using PersonalAIAssistant.Memory.Core.Interfaces.Security;
using PersonalAIAssistant.Memory.Core.Models;
using PersonalAIAssistant.Memory.Infrastructure.Security;
using PersonalAIAssistant.Memory.Infrastructure.AI;
using PersonalAIAssistant.Memory.Infrastructure.AI.Gemini;
using PersonalAIAssistant.Memory.Infrastructure.AI.OpenAi;
using PersonalAIAssistant.Memory.Infrastructure.AI.Teams;
using PersonalAIAssistant.Memory.Infrastructure.EF;
using PersonalAIAssistant.Memory.Infrastructure.Sql;
using PersonalAIAssistant.Memory.Infrastructure.Events;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;
using PersonalAIAssistant.Memory.Events;
using MassTransit;
using Qdrant.Client;

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
            // Register EF Core Read Model DbContext & Segregated Repositories (ISP & DIP)
            services.AddDbContext<ReadModelDbContext>(configureDbContext);
            services.AddScoped<SqlReadModelRepository>();
            services.AddScoped<IReadModelRepository>(sp => sp.GetRequiredService<SqlReadModelRepository>());
            services.AddScoped<IEventIdempotencyStore>(sp => sp.GetRequiredService<SqlReadModelRepository>());
            services.AddScoped<IProcessingLockStore>(sp => sp.GetRequiredService<SqlReadModelRepository>());
            services.AddScoped<IRetentionQueryStore>(sp => sp.GetRequiredService<SqlReadModelRepository>());
            services.AddScoped<ITransactionalReadModelRepository>(sp => sp.GetRequiredService<SqlReadModelRepository>());

            // Register MongoDB client, Database & Event Store / Snapshot Repository
            services.AddSingleton<IMongoClient>(new MongoClient(mongoConnectionString));
            services.AddSingleton<IMongoDatabase>(sp =>
            {
                var client = sp.GetRequiredService<IMongoClient>();
                return client.GetDatabase(mongoDatabaseName);
            });
            services.AddSingleton<IEventStore, MongoEventStore>();
            services.AddSingleton<ISnapshotRepository, MongoSnapshotRepository>();

            return services;
        }

        // ─────────────────────────────────────────────────────────────────────
        // AI Providers + Teams Webhook
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Registers OpenAI, Gemini, and Teams Webhook services.
        /// </summary>
        public static IServiceCollection AddAiProviders(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // ── Bind strongly-typed options ──────────────────────────────────
            services.Configure<AiProviderOptions>(configuration.GetSection(AiProviderOptions.SectionName));
            services.Configure<AiGovernanceOptions>(configuration.GetSection(AiGovernanceOptions.SectionName));
            services.AddSingleton<IAiGovernanceValidator, CorporateAiGovernanceValidator>();
            services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
            services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
            services.Configure<TeamsOptions>(configuration.GetSection(TeamsOptions.SectionName));
            
            services.Configure<ChunkingOptions>(configuration.GetSection("Chunking"));
            services.Configure<RetentionOptions>(configuration.GetSection(RetentionOptions.SectionName));
            services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
            services.Configure<CmekOptions>(configuration.GetSection(CmekOptions.SectionName));
            services.Configure<DlpOptions>(configuration.GetSection(DlpOptions.SectionName));
            services.AddSingleton<IDataLossPreventionService, RuleBasedDlpService>();

            // ── Named HttpClients with Polly Resilience ──────────────────────
            services.AddHttpClient("openai", client =>
            {
                client.BaseAddress = new Uri("https://api.openai.com/v1/");
                client.Timeout     = TimeSpan.FromSeconds(60);
            }).AddStandardResilienceHandler();

            services.AddHttpClient("gemini", client =>
            {
                client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
                client.Timeout     = TimeSpan.FromSeconds(60);
            }).AddStandardResilienceHandler();

            services.AddHttpClient("teams", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            }).AddStandardResilienceHandler();

            // ── Provider implementations ─────────────────────────────────────
            services.AddScoped<IAIProvider, OpenAiChatProvider>();
            services.AddScoped<IAIProvider, GeminiChatProvider>();
            services.AddScoped<IAIProviderFactory, AIProviderFactory>();

            // Embeddings and LLM Compression Services
            services.AddScoped<IEmbeddingService, PersonalAIAssistant.Memory.Infrastructure.AI.OpenAi.OpenAiEmbeddingService>();
            services.AddScoped<ICompressionService, PersonalAIAssistant.Memory.Infrastructure.AI.LlmCompressionService>();

            // Teams notification sender
            services.AddScoped<INotificationSender, TeamsWebhookSender>();

            // ── Memory features (Chunking, Metrics, Retrieval, Scorer) ───────
            services.AddSingleton<IAiMetricsLogger, AiMetricsLogger>();
            services.AddSingleton<ITextChunker, TextChunker>();
            services.AddSingleton<IRerankingScorer, DefaultRerankingScorer>();
            services.AddScoped<IMemoryRetrievalService, MemoryRetrievalService>();
            services.AddSingleton<PersonalAIAssistant.Memory.Infrastructure.Security.AesEncryptionService>();
            services.AddSingleton<IEncryptionService, PersonalAIAssistant.Memory.Infrastructure.Security.AesGcmEncryptionService>();

            // ── Vector Database (Qdrant) ─────────────────────────────────────
            services.Configure<QdrantOptions>(configuration.GetSection(QdrantOptions.SectionName));
            services.AddSingleton(sp =>
            {
                var qdrantOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QdrantOptions>>().Value;
                return new QdrantClient(host: qdrantOpts.Host, port: qdrantOpts.Port, https: qdrantOpts.Https, apiKey: qdrantOpts.ApiKey);
            });
            var useInMemory = configuration.GetValue<bool>("UseInMemoryStore", false);
            if (useInMemory)
            {
                services.AddSingleton<IVectorMemoryRepository, PersonalAIAssistant.Memory.Infrastructure.InMemory.InMemoryVectorMemoryRepository>();
            }
            else
            {
                services.AddScoped<IVectorMemoryRepository, QdrantVectorRepository>();
            }

            // ── Message Broker (MassTransit + RabbitMQ) ──────────────────────
            if (!useInMemory)
            {
                services.AddMassTransit(x =>
                {
                    x.AddConsumer<MemoryEventConsumer<MemoryAddedEvent>>();
                    x.AddConsumer<MemoryEventConsumer<MemoryCompressedEvent>>();
                    x.AddConsumer<MemoryEventConsumer<MemoryConsolidatedEvent>>();
                    x.AddConsumer<MemoryEventConsumer<MemoryIndexedEvent>>();
                    x.AddConsumer<MemoryEventConsumer<MemoryArchivedEvent>>();
                    x.AddConsumer<MemoryEventConsumer<MemoryDeletedEvent>>();
                    x.AddConsumer<MemoryEventConsumer<SnapshotCreatedEvent>>();

                    x.UsingRabbitMq((context, cfg) =>
                    {
                        var rabbitHost = configuration["MessageBroker:Host"] ?? "localhost";
                        var rabbitUser = configuration["MessageBroker:Username"] ?? "guest";
                        var rabbitPass = configuration["MessageBroker:Password"] ?? "guest";

                        cfg.Host(rabbitHost, "/", h =>
                        {
                            h.Username(rabbitUser);
                            h.Password(rabbitPass);
                        });

                        cfg.ConfigureEndpoints(context);
                    });
                });

                services.Configure<MassTransitHostOptions>(options =>
                {
                    options.WaitUntilStarted = false;
                    options.StartTimeout = TimeSpan.FromSeconds(2);
                    options.StopTimeout = TimeSpan.FromSeconds(2);
                });

                services.AddScoped<IEventBus, RabbitMQEventBus>();
            }

            return services;
        }
    }
}
