using Application.Common;
using Application.Common.Identity;
using AwsShowcase.Core.Abstractions;
using AwsShowcase.Integration.Handlers;
using AwsShowcase.Integration.Persistence;
using Domain.Common.Dates;
using Domain.Common.Identity;
using Domain.Common.Repositories;
using Domain.Common.Settings;
using Infrastructure.Common.AWS.Metrics;
using Infrastructure.Common.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Persistence.Common.AWS.DependencyInjection;
using Utils.Common.Crypto;

namespace AwsShowcase.Integration;

public static class DependencyInjection
{
    /// <summary>
    /// Composition root: every AWS integration is registered through the LIBRARY's
    /// own AddXxx extension methods - the application only composes, it contains
    /// no wiring logic of its own.
    /// </summary>
    public static IServiceCollection AddShowcaseIntegration(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        // Identity + clock for audit stamping ("Showcase:UseHttpUser" switches to
        // the claims-based HttpCurrentUser from the library).
        services.AddHttpContextAccessor();
        if (configuration.GetValue<bool>("Showcase:UseHttpUser"))
        {
            services.AddScoped<ICurrentUser, HttpCurrentUser>();
        }
        else
        {
            services.AddSingleton<ICurrentUser, DemoCurrentUser>();
        }
        services.AddSingleton<IDateTime, UtcDateTime>();
        services.Configure<AwsTagsConfigSettings>(configuration.GetSection("AwsTags"));

        // DynamoDB EF-style persistence + outbox + distributed lock (library methods)
        services.AddPersistenceDynamoDb<ShowcaseContext>(configuration, environment);
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ShowcaseContext>());
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IIntegrationEventOutbox, TransactionalOutbox>();
        services.AddDynamoDbOutbox();
        services.AddDynamoDbDistributedLock();

        // AWS services - one library call each
        services.AddAwsFileStorage(configuration);          // S3 (+ presigned URLs)
        services.AddAwsEmail(configuration);                // SES
        services.AddEventBridgePublishing(configuration);   // EventBridge (feeds the outbox dispatcher)
        services.AddEventBridgeScheduling(configuration);   // EventBridge Scheduler
        services.AddStepFunctionsWorkflows(configuration);  // Step Functions
        services.AddAwsStreaming(configuration);            // Kinesis + Firehose
        services.AddCognitoIdentity(configuration);         // Cognito user administration
        services.AddAwsSnsSqsEventBus(configuration);       // SNS topics + SQS queues/DLQs + subscriptions
        services.AddAwsSecrets(configuration);              // Secrets Manager + KMS
        services.AddAwsApiService(configuration);           // service-to-service HTTP + Cognito tokens
        services.AddEmfMetrics(new EmfMetricsOptions        // CloudWatch metrics (EMF)
        {
            Namespace = "AwsShowcase",
            DefaultDimensions = new Dictionary<string, string> { ["Service"] = "showcase" }
        });

        // Feature flags: "Showcase:FeatureProvider" = Ssm (default) | AppConfig | Local
        switch (configuration["Showcase:FeatureProvider"]?.ToLowerInvariant())
        {
            case "appconfig":
                services.AddAppConfigFeatureFlags(configuration);
                break;
            case "local":
                services.AddLocalFeatureFlags();
                break;
            default:
                services.AddSsmFeatureFlags(configuration);
                break;
        }

        // Cache: "Showcase:CacheProvider" = Memory (default) | Redis (ElastiCache-ready)
        if (string.Equals(configuration["Showcase:CacheProvider"], "Redis", StringComparison.OrdinalIgnoreCase))
        {
            services.AddAwsRedisCache(configuration);
        }
        else
        {
            services.AddDistributedMemoryCache();
            services.AddDistributedCacheAdapter();
        }

        // Named HTTP client so IApiService can call this API itself in the demo
        services.AddHttpClient("Showcase", client =>
            client.BaseAddress = new Uri(configuration["Showcase:SelfBaseUrl"] ?? "http://localhost:8080"));

        // Crypto utilities (demo key; production: GetCryptoOptions + KMS-wrapped key)
        services.AddSingleton<ICryptoProvider>(new CryptoProvider(null));

        // Event handlers used by the bus subscription demo
        services.AddTransient<OrderCreatedEventHandler>();
        services.AddTransient<DynamicLoggingEventHandler>();

        // Consumption: in-process SQS consumer (library BackgroundService) polls the
        // event queues and invokes the handlers - the local/container alternative to
        // the QueueEventDispatcher Lambda. Subscriptions are registered at startup.
        services.AddSqsConsumer(configuration);
        services.AddHostedService<EventBusInitializer>();

        return services;
    }
}
