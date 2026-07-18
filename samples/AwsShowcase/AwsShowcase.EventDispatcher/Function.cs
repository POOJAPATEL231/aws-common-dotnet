using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Application.Common.Event;
using AwsShowcase.Entity;
using AwsShowcase.Integration.Handlers;
using Infrastructure.Common.AWS.Eventbus;
using Infrastructure.Common.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace AwsShowcase.EventDispatcher;

/// <summary>
/// The "QueueEventDispatcher" Lambda that AwsEventBusPersisterConnection wires as the
/// event source of every "{event}_Queue.fifo" queue (via AwsInfraSettings:
/// EventDispatcherFunctionName). It receives SQS batches and hands each record to the
/// library's ISqsMessageDispatcher, which routes it to the registered handlers.
/// Failed records are reported individually (partial batch response), so SQS retries
/// only them and eventually parks poison messages in the DLQ.
///
/// Deploy: dotnet lambda deploy-function QueueEventDispatcher
/// (requires Amazon.Lambda.Tools: dotnet tool install -g Amazon.Lambda.Tools)
/// </summary>
public class Function
{
    private static readonly ServiceProvider _services = BuildServiceProvider();
    private static readonly SemaphoreSlim _subscriptionGate = new(1, 1);
    private static bool _subscriptionsRegistered;

    public async Task<SQSBatchResponse> FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        await EnsureSubscriptionsRegisteredAsync();

        var failures = new List<SQSBatchResponse.BatchItemFailure>();

        using var scope = _services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISqsMessageDispatcher>();

        foreach (var record in sqsEvent.Records)
        {
            try
            {
                await dispatcher.DispatchAsync(record.Body);
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Dispatch failed for message {record.MessageId}: {ex.Message}");
                failures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = record.MessageId });
            }
        }

        return new SQSBatchResponse { BatchItemFailures = failures };
    }

    /// <summary>
    /// The dispatcher hosts its own handler registrations: at cold start it records
    /// which handler serves which event in its subscriptions manager. Add one line
    /// here per event this function should process.
    /// </summary>
    private static async Task EnsureSubscriptionsRegisteredAsync()
    {
        if (_subscriptionsRegistered)
        {
            return;
        }

        await _subscriptionGate.WaitAsync();
        try
        {
            if (_subscriptionsRegistered)
            {
                return;
            }

            using var scope = _services.CreateScope();
            var subscriptions = scope.ServiceProvider.GetRequiredService<IAsyncEventBusSubscriptionsManager>();

            await subscriptions.AddSubscriptionAsync<OrderCreatedIntegrationEvent, OrderCreatedEventHandler>();

            _subscriptionsRegistered = true;
        }
        finally
        {
            _subscriptionGate.Release();
        }
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.AddConsole());

        // Subscription bookkeeping lives in a local memory cache - this function OWNS
        // its handler registrations (see EnsureSubscriptionsRegisteredAsync).
        services.AddDistributedMemoryCache();
        services.AddDistributedCacheAdapter();

        // Library consumption-side registration: subscriptions manager + dispatcher.
        services.AddSqsEventDispatch();

        // The handlers this dispatcher hosts.
        services.AddTransient<OrderCreatedEventHandler>();
        services.AddTransient<DynamicLoggingEventHandler>();

        return services.BuildServiceProvider();
    }
}
