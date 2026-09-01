using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PersonalAIAssistant.Memory.Business.Behaviors;
using PersonalAIAssistant.Memory.Business.EventHandlers;
using PersonalAIAssistant.Memory.Business.Projectors;
using PersonalAIAssistant.Memory.Business.Workers;
using PersonalAIAssistant.Memory.Business.Security;
using PersonalAIAssistant.Memory.Core.Interfaces.Messaging;
using PersonalAIAssistant.Memory.Events;
using System.Reflection;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;

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
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(DataLossPreventionBehavior<,>));
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            });

            // Resilience Pipelines
            services.AddResiliencePipeline("WorkerRetry", builder =>
            {
                builder.AddRetry(new RetryStrategyOptions
                {
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                    Delay = TimeSpan.FromSeconds(2),
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true
                });
            });

            services.AddResiliencePipeline("AiServiceProtection", builder =>
            {
                builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(10),
                    MinimumThroughput = 5,
                    BreakDuration = TimeSpan.FromSeconds(30)
                });
                builder.AddRetry(new RetryStrategyOptions
                {
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                    Delay = TimeSpan.FromSeconds(1),
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true
                });
            });

            // FluentValidation validators
            services.AddValidatorsFromAssembly(assembly);

            // Scoped event handlers & projectors (untyped and typed subscribers)
            services.AddScoped<IMemoryEventHandler, MemoryEventProjector>();
            services.AddScoped<IMemoryEventHandler<MemoryAddedEvent>, EmbeddingIndexingEventHandler>();
            services.AddScoped<IMemoryEventHandler<MemoryConsolidatedEvent>, MemoryConsolidatedNotificationHandler>();

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
            services.AddHostedService<RetentionWorker>();

            return services;
        }
    }
}
