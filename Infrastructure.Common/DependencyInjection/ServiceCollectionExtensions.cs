using Amazon.AppConfigData;
using Amazon.CognitoIdentityProvider;
using Amazon.EventBridge;
using Amazon.KeyManagementService;
using Amazon.Kinesis;
using Amazon.KinesisFirehose;
using Amazon.Lambda;
using Amazon.S3;
using Amazon.Scheduler;
using Amazon.SecretsManager;
using Amazon.SimpleEmailV2;
using Amazon.SimpleNotificationService;
using Amazon.SimpleSystemsManagement;
using Amazon.SQS;
using Amazon.StepFunctions;
using Application.Common;
using Application.Common.AWS;
using Application.Common.Email;
using Application.Common.Event;
using Application.Common.FileProvider;
using Application.Common.Identity;
using Application.Common.Metrics;
using Application.Common.Scheduling;
using Application.Common.Streaming;
using Application.Common.Workflow;
using Infrastructure.Common.AWS.ApiService;
using Infrastructure.Common.AWS.Email;
using Infrastructure.Common.AWS.Eventbus;
using Infrastructure.Common.AWS.FeatureManager;
using Infrastructure.Common.AWS.FileProvider;
using Infrastructure.Common.AWS.Identity;
using Infrastructure.Common.AWS.Metrics;
using Infrastructure.Common.AWS.Scheduling;
using Infrastructure.Common.AWS.Streaming;
using Infrastructure.Common.AWS.Workflow;
using Infrastructure.Common.Cache;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Utils.Common.Crypto;

namespace Infrastructure.Common.DependencyInjection
{
    /// <summary>
    /// One-line registration for every AWS integration in the library, so host
    /// applications only compose - all wiring logic lives here. Each method also
    /// registers the underlying AWS client, transparently switching to LocalStack
    /// when "LocalStack:UseLocalStack" is true.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>SES email behind <see cref="IEmailService"/>.</summary>
        public static IServiceCollection AddAwsEmail(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAwsServiceWithConfiguration<IAmazonSimpleEmailServiceV2>(configuration);
            services.AddScoped<IEmailService, SesEmailService>();
            return services;
        }

        /// <summary>S3 object storage (incl. presigned URLs) behind <see cref="IFileProvider"/>.</summary>
        public static IServiceCollection AddAwsFileStorage(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAwsServiceWithConfiguration<IAmazonS3>(configuration);
            services.AddScoped<IFileProvider, AwsS3FileProvider>();
            return services;
        }

        /// <summary>EventBridge publishing behind <see cref="IIntegrationEventPublisher"/> (options from "EventBridge" section).</summary>
        public static IServiceCollection AddEventBridgePublishing(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAwsServiceWithConfiguration<IAmazonEventBridge>(configuration);
            services.AddSingleton(configuration.GetSection("EventBridge").Get<EventBridgeOptions>() ?? new EventBridgeOptions());
            services.AddScoped<IIntegrationEventPublisher, EventBridgeEventBus>();
            return services;
        }

        /// <summary>EventBridge Scheduler behind <see cref="IScheduler"/>.</summary>
        public static IServiceCollection AddEventBridgeScheduling(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAwsServiceWithConfiguration<IAmazonScheduler>(configuration);
            services.AddScoped<IScheduler, EventBridgeScheduler>();
            return services;
        }

        /// <summary>Step Functions behind <see cref="IWorkflowClient"/>.</summary>
        public static IServiceCollection AddStepFunctionsWorkflows(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAwsServiceWithConfiguration<IAmazonStepFunctions>(configuration);
            services.AddScoped<IWorkflowClient, StepFunctionsWorkflowClient>();
            return services;
        }

        /// <summary>Kinesis Data Streams + Data Firehose publishers (both <see cref="IStreamPublisher"/> implementations).</summary>
        public static IServiceCollection AddAwsStreaming(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAwsServiceWithConfiguration<IAmazonKinesis>(configuration);
            services.AddAwsServiceWithConfiguration<IAmazonKinesisFirehose>(configuration);
            services.AddScoped<KinesisStreamPublisher>();
            services.AddScoped<FirehoseStreamPublisher>();
            return services;
        }

        /// <summary>Cognito user administration behind <see cref="IIdentityService"/> (options from "Cognito" section).</summary>
        public static IServiceCollection AddCognitoIdentity(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAwsServiceWithConfiguration<IAmazonCognitoIdentityProvider>(configuration);
            services.AddSingleton(configuration.GetSection("Cognito").Get<CognitoIdentityOptions>() ?? new CognitoIdentityOptions());
            services.AddScoped<IIdentityService, CognitoIdentityService>();
            return services;
        }

        /// <summary>CloudWatch metrics via Embedded Metric Format behind <see cref="IMetrics"/>.</summary>
        public static IServiceCollection AddEmfMetrics(this IServiceCollection services, EmfMetricsOptions? options = null)
        {
            services.AddSingleton<IMetrics>(new EmfMetrics(options ?? new EmfMetricsOptions()));
            return services;
        }

        /// <summary>SSM Parameter Store feature flags (/Features/) behind <see cref="IFeatureManager"/>.</summary>
        public static IServiceCollection AddSsmFeatureFlags(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAwsServiceWithConfiguration<IAmazonSimpleSystemsManagement>(configuration);
            services.AddScoped<IFeatureManager, AwsFeatureManager>();
            return services;
        }

        /// <summary>AWS AppConfig feature flags behind <see cref="IFeatureManager"/> (options from "AppConfig" section).</summary>
        public static IServiceCollection AddAppConfigFeatureFlags(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAwsServiceWithConfiguration<IAmazonAppConfigData>(configuration);
            services.AddSingleton(configuration.GetSection("AppConfig").Get<AppConfigOptions>() ?? new AppConfigOptions());
            services.AddSingleton<IFeatureManager, AppConfigFeatureManager>();
            return services;
        }

        /// <summary>Configuration-file feature flags behind <see cref="IFeatureManager"/> - for local development.</summary>
        public static IServiceCollection AddLocalFeatureFlags(this IServiceCollection services)
        {
            services.AddSingleton<IFeatureManager, LocalFeatureManager>();
            return services;
        }

        /// <summary>
        /// Full SNS/SQS event bus: topic-per-event publishing, queue+DLQ provisioning,
        /// cache-backed subscription routing and the SQS queue service. Queue tuning
        /// comes from the "AwsInfraSettings:Queues" section.
        /// </summary>
        public static IServiceCollection AddAwsSnsSqsEventBus(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<AWS.AwsInfraSettings>(configuration.GetSection("AwsInfraSettings"));
            services.AddAwsServiceWithConfiguration<IAmazonSQS>(configuration);
            services.AddAwsServiceWithConfiguration<IAmazonSimpleNotificationService>(configuration);
            services.AddAwsServiceWithConfiguration<IAmazonLambda>(configuration);
            services.AddSingleton<IEventBusPersisterConnection, AwsEventBusPersisterConnection>();
            services.AddScoped<IAsyncEventBusSubscriptionsManager, EventBusSubscriptionsManager>();
            services.AddSingleton<IEventBusSubscriptionsManager, InMemoryEventBusSubscriptionsManager>();
            services.AddScoped<IEventBus, AwsEventBus>();
            services.AddScoped<IQueueService, SimpleQueueService>();
            services.AddScoped<ISqsMessageDispatcher, SqsMessageDispatcher>();
            return services;
        }

        /// <summary>
        /// Consumption-side registration for hosts that only dispatch queued events to
        /// handlers (e.g. the QueueEventDispatcher Lambda): the cache-backed
        /// subscriptions manager plus <see cref="ISqsMessageDispatcher"/>. Requires an
        /// <see cref="ICache"/> registration (memory or Redis).
        /// </summary>
        public static IServiceCollection AddSqsEventDispatch(this IServiceCollection services)
        {
            services.AddScoped<IAsyncEventBusSubscriptionsManager, EventBusSubscriptionsManager>();
            services.AddScoped<ISqsMessageDispatcher, SqsMessageDispatcher>();
            return services;
        }

        /// <summary>Secrets Manager + KMS key wrapping behind <see cref="ISecretRepository"/>.</summary>
        public static IServiceCollection AddAwsSecrets(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAwsServiceWithConfiguration<IAmazonSecretsManager>(configuration);
            services.AddAwsServiceWithConfiguration<IAmazonKeyManagementService>(configuration);
            services.AddScoped<ISecretRepository, AWS.SecretRepository.AwsSecretRepository>();
            return services;
        }

        /// <summary>
        /// Service-to-service HTTP client (<see cref="IApiService"/>) with auth forwarding
        /// and Cognito client-credentials support. Register one named HttpClient per
        /// logical service the application calls.
        /// </summary>
        public static IServiceCollection AddAwsApiService(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpContextAccessor();
            services.Configure<Application.Common.Settings.AppSettings>(configuration.GetSection("AppSettings"));
            services.Configure<Domain.Common.Settings.CognitoAuthSettings>(configuration.GetSection("CognitoAuth"));

            var cognitoBaseUrl = configuration["CognitoAuth:BaseUrl"];
            services.AddHttpClient<ICognitoHttpClient, CognitoHttpClient>(client =>
            {
                if (!string.IsNullOrWhiteSpace(cognitoBaseUrl))
                {
                    client.BaseAddress = new Uri(cognitoBaseUrl);
                }
            });

            services.AddScoped<IApiService, AwsApiService>();
            return services;
        }

        /// <summary>
        /// Resilient Redis cache (Polly retries, forced reconnect) behind <see cref="ICache"/> -
        /// works with ElastiCache or any Redis endpoint ("Redis:ConnectionString").
        /// </summary>
        public static IServiceCollection AddAwsRedisCache(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton(new RedisCacheOptions
            {
                ConnectionString = configuration["Redis:ConnectionString"] ?? "localhost:6379",
                ReconnectMinIntervalSeconds = configuration.GetValue("Redis:ReconnectMinIntervalSeconds", 5),
                ReconnectErrorThresholdSeconds = configuration.GetValue("Redis:ReconnectErrorThresholdSeconds", 30),
                RestartConnectionTimeoutSeconds = configuration.GetValue("Redis:RestartConnectionTimeoutSeconds", 10),
                RetryMaxAttempts = configuration.GetValue("Redis:RetryMaxAttempts", 3)
            });
            services.AddSingleton<ICache, RedisCache>();
            return services;
        }

        /// <summary>IDistributedCache-backed <see cref="ICache"/> (memory, Redis, SQL - whatever is registered).</summary>
        public static IServiceCollection AddDistributedCacheAdapter(this IServiceCollection services)
        {
            services.AddScoped<ICache, Cache.Cache>();
            return services;
        }
    }
}
