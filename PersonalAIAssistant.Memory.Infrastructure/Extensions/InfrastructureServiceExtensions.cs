using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using PersonalAIAssistant.Memory.Core.Interfaces.Mongo;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Core.Interfaces.Sql;
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
    }
}
