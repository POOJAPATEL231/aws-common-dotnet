using AwsShowcase.Entity;
using AwsShowcase.Integration.Handlers;
using Infrastructure.Common.AWS.Eventbus;

namespace AwsShowcase.Integration;

/// <summary>
/// Single place that declares every event → handler subscription for the application.
/// Called at startup by <see cref="EventBusInitializer"/> so all topics, queues and
/// DLQs are provisioned and all handler routing is registered in one shot. Add one
/// line here for each new integration event the app consumes.
/// </summary>
public static class EventRegistration
{
    public static async Task RegisterAllAsync(IEventBus bus, CancellationToken cancellationToken = default)
    {
        await bus.SubscribeAsync<OrderCreatedIntegrationEvent, OrderCreatedEventHandler>();
        // await bus.SubscribeAsync<NextEvent, NextEventHandler>();
        // await bus.SubscribeDynamicAsync<DynamicLoggingEventHandler>("some-external-event");
    }
}
