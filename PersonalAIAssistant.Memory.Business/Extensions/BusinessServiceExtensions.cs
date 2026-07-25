using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PersonalAIAssistant.Memory.Business.Behaviors;
using PersonalAIAssistant.Memory.Business.EventHandlers;
using PersonalAIAssistant.Memory.Business.Projectors;
using PersonalAIAssistant.Memory.Business.Workers;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using System.Reflection;

namespace PersonalAIAssistant.Memory.Business.Extensions
{
    public static class BusinessServiceExtensions
    {
        public static IServiceCollection AddMemoryBusinessServices(
            this IServiceCollection services,
            Action<ConsolidationWorkerOptions>? configureConsolidation = null,
            Action<SnapshotWorkerOptions>? configureSnapshot = null)
        {
            var assembly = Assembly.GetExecutingAssembly();

            // MediatR command and query handlers along with pipeline behaviors
            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(assembly);
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            });

            // FluentValidation validators
            services.AddValidatorsFromAssembly(assembly);

            // Scoped event handlers (projectors & indexing handlers for the Event Bus)
            services.AddScoped<IMemoryEventHandler, MemoryEventProjector>();
            services.AddScoped<IMemoryEventHandler, EmbeddingIndexingEventHandler>();

            // Worker options and background services
            if (configureConsolidation != null)
                services.Configure(configureConsolidation);
            else
                services.Configure<ConsolidationWorkerOptions>(_ => { });

            if (configureSnapshot != null)
                services.Configure(configureSnapshot);
            else
                services.Configure<SnapshotWorkerOptions>(_ => { });

            services.AddHostedService<ConsolidationWorker>();
            services.AddHostedService<SnapshotWorker>();

            return services;
        }
    }
}
