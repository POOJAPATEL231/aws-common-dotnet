using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using AwsShowcase.Entity;
using AwsShowcase.Integration.Handlers;
using Infrastructure.Common.AWS.Eventbus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AwsShowcase.Integration;

/// <summary>
/// Provisions the showcase's demo resources at startup so every endpoint works
/// out of the box:
///   - subscribes handlers on the event bus (creates the SNS topic + SQS queue + DLQ
///     and records the handler routing), enabling publish -> SNS -> SQS -> consumer -> handler
///   - creates the demo Kinesis stream (streams, unlike S3 buckets / SQS queues /
///     DynamoDB tables, are not auto-created by the publisher - they are provisioned
///     infrastructure in production)
/// Skipped gracefully when the AWS endpoint (LocalStack) is unreachable.
/// </summary>
public class EventBusInitializer : IHostedService
{
    /// <summary>Well-known demo stream name the StreamsController targets by default.</summary>
    public const string DemoKinesisStream = "showcase-stream";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventBusInitializer> _logger;

    public EventBusInitializer(IServiceScopeFactory scopeFactory, ILogger<EventBusInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        await SubscribeHandlersAsync(scope.ServiceProvider, cancellationToken);
        await EnsureDemoKinesisStreamAsync(scope.ServiceProvider, cancellationToken);
    }

    private async Task SubscribeHandlersAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            var bus = services.GetRequiredService<IEventBus>();
            // One call registers every event -> handler subscription for the app.
            await EventRegistration.RegisterAllAsync(bus, cancellationToken);
            _logger.LogInformation("Event bus ready: all event subscriptions registered.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Event bus subscription skipped ({Message}). Start LocalStack and restart to enable the event loop.", ex.Message);
        }
    }

    private async Task EnsureDemoKinesisStreamAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            var kinesis = services.GetRequiredService<IAmazonKinesis>();
            try
            {
                await kinesis.DescribeStreamSummaryAsync(new DescribeStreamSummaryRequest { StreamName = DemoKinesisStream }, cancellationToken);
                return; // already exists
            }
            catch (ResourceNotFoundException)
            {
                await kinesis.CreateStreamAsync(new CreateStreamRequest { StreamName = DemoKinesisStream, ShardCount = 1 }, cancellationToken);
                _logger.LogInformation("Created demo Kinesis stream '{Stream}'.", DemoKinesisStream);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Demo Kinesis stream provisioning skipped ({Message}).", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
