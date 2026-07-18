using Application.Common.Event;
using AwsShowcase.Entity;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AwsShowcase.Integration.Handlers;

/// <summary>Typed integration-event handler used by the SNS/SQS bus subscription demo.</summary>
public class OrderCreatedEventHandler : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
{
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(ILogger<OrderCreatedEventHandler> logger) => _logger = logger;

    public Task Handle(OrderCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Order {OrderId} created for {Customer} - handled from the event bus.",
            integrationEvent.OrderId, integrationEvent.CustomerEmail);
        return Task.CompletedTask;
    }
}

/// <summary>Dynamic (untyped) handler used by the dynamic-subscription demo.</summary>
public class DynamicLoggingEventHandler : IDynamicIntegrationEventHandler
{
    private readonly ILogger<DynamicLoggingEventHandler> _logger;

    public DynamicLoggingEventHandler(ILogger<DynamicLoggingEventHandler> logger) => _logger = logger;

    public Task Handle(JsonDocument eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Dynamic event received: {Payload}", eventData.RootElement.ToString());
        return Task.CompletedTask;
    }
}
