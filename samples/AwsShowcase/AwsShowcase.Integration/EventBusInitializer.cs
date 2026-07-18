using AwsShowcase.Entity;
using AwsShowcase.Integration.Handlers;
using Infrastructure.Common.AWS.Eventbus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AwsShowcase.Integration;

/// <summary>
/// Subscribes the showcase's event handlers at startup so the full event loop
/// (publish -> SNS -> SQS -> consumer -> handler) works immediately: the
/// subscription provisions the topic/queue/DLQ and records the handler routing.
/// Skipped gracefully when the AWS endpoint (LocalStack) is unreachable.
/// </summary>
public class EventBusInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventBusInitializer> _logger;

    public EventBusInitializer(IServiceScopeFactory scopeFactory, ILogger<EventBusInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
            await bus.SubscribeAsync<OrderCreatedIntegrationEvent, OrderCreatedEventHandler>();
            _logger.LogInformation("Event bus ready: OrderCreatedIntegrationEvent -> OrderCreatedEventHandler.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Event bus subscription skipped ({Message}). Start LocalStack and restart to enable the event loop.", ex.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
