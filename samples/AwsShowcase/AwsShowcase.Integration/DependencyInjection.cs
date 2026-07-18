using Amazon.CognitoIdentityProvider;
using Amazon.EventBridge;
using Amazon.Kinesis;
using Amazon.KinesisFirehose;
using Amazon.S3;
using Amazon.Scheduler;
using Amazon.SimpleEmailV2;
using Amazon.SimpleSystemsManagement;
using Amazon.StepFunctions;
using Application.Common;
using Application.Common.AWS;
using Application.Common.Email;
using Application.Common.Event;
using Application.Common.FileProvider;
using Application.Common.Identity;
using Application.Common.Metrics;
using Application.Common.Scheduling;
using Application.Common.Workflow;
using AwsShowcase.Core.Abstractions;
using AwsShowcase.Integration.Persistence;
using Domain.Common.Dates;
using Domain.Common.Identity;
using Domain.Common.Repositories;
using Domain.Common.Settings;
using Infrastructure.Common.AWS.Email;
using Infrastructure.Common.AWS.Eventbus;
using Infrastructure.Common.AWS.FeatureManager;
using Infrastructure.Common.AWS.FileProvider;
using Infrastructure.Common.AWS.Identity;
using Infrastructure.Common.AWS.Metrics;
using Infrastructure.Common.AWS.Scheduling;
using Infrastructure.Common.AWS.Streaming;
using Infrastructure.Common.AWS.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Persistence.Common.AWS.DependencyInjection;
using Utils.Common.Crypto;

namespace AwsShowcase.Integration;

public static class DependencyInjection
{
    /// <summary>
    /// Wires every library service behind its abstraction. Each AWS client
    /// transparently switches to LocalStack when "LocalStack:UseLocalStack" is true.
    /// </summary>
    public static IServiceCollection AddShowcaseIntegration(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        // Identity + clock used for audit stamping
        services.AddSingleton<ICurrentUser, DemoCurrentUser>();
        services.AddSingleton<IDateTime, UtcDateTime>();
        services.Configure<AwsTagsConfigSettings>(configuration.GetSection("AwsTags"));

        // DynamoDB EF-style persistence: context, sets, table creation - plus the
        // transactional outbox and the DynamoDB-backed distributed lock.
        services.AddPersistenceDynamoDb<ShowcaseContext>(configuration, environment);
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ShowcaseContext>());
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IIntegrationEventOutbox, TransactionalOutbox>();
        services.AddDynamoDbOutbox();
        services.AddDynamoDbDistributedLock();

        // AWS clients (LocalStack-aware)
        services.AddAwsServiceWithConfiguration<IAmazonS3>(configuration);
        services.AddAwsServiceWithConfiguration<IAmazonSimpleEmailServiceV2>(configuration);
        services.AddAwsServiceWithConfiguration<IAmazonEventBridge>(configuration);
        services.AddAwsServiceWithConfiguration<IAmazonScheduler>(configuration);
        services.AddAwsServiceWithConfiguration<IAmazonStepFunctions>(configuration);
        services.AddAwsServiceWithConfiguration<IAmazonKinesis>(configuration);
        services.AddAwsServiceWithConfiguration<IAmazonKinesisFirehose>(configuration);
        services.AddAwsServiceWithConfiguration<IAmazonCognitoIdentityProvider>(configuration);
        services.AddAwsServiceWithConfiguration<IAmazonSimpleSystemsManagement>(configuration);

        // Library services behind their abstractions
        services.AddScoped<IFileProvider, AwsS3FileProvider>();                 // S3 (incl. presigned URLs)
        services.AddScoped<IEmailService, SesEmailService>();                   // SES
        services.AddSingleton(configuration.GetSection("EventBridge").Get<EventBridgeOptions>() ?? new EventBridgeOptions());
        services.AddScoped<IIntegrationEventPublisher, EventBridgeEventBus>();  // EventBridge (feeds the outbox dispatcher)
        services.AddScoped<IScheduler, EventBridgeScheduler>();                 // EventBridge Scheduler
        services.AddScoped<IWorkflowClient, StepFunctionsWorkflowClient>();     // Step Functions
        services.AddScoped<KinesisStreamPublisher>();                           // Kinesis
        services.AddScoped<FirehoseStreamPublisher>();                          // Firehose
        services.AddSingleton(configuration.GetSection("Cognito").Get<CognitoIdentityOptions>() ?? new CognitoIdentityOptions());
        services.AddScoped<IIdentityService, CognitoIdentityService>();         // Cognito admin
        services.AddScoped<IFeatureManager, AwsFeatureManager>();               // SSM /Features/ flags
        services.AddSingleton<IMetrics>(new EmfMetrics(new EmfMetricsOptions    // CloudWatch EMF metrics
        {
            Namespace = "AwsShowcase",
            DefaultDimensions = new Dictionary<string, string> { ["Service"] = "showcase" }
        }));

        // ICache over IDistributedCache. Swap AddDistributedMemoryCache for Redis /
        // ElastiCache in production - the library's RedisCache also implements ICache.
        services.AddDistributedMemoryCache();
        services.AddScoped<ICache, Infrastructure.Common.Cache.Cache>();

        // Crypto utilities (demo key; production would use GetCryptoOptions + a KMS-wrapped key)
        services.AddSingleton<ICryptoProvider>(new CryptoProvider(null));

        return services;
    }
}
