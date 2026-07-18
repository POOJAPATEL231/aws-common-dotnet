using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Application.Common.Event;
using Domain.Common.Event;
using System.Text.Json;

namespace Infrastructure.Common.AWS.Eventbus
{
    public record EventBridgeOptions
    {
        /// <summary>Target event bus; "default" unless a custom bus is used.</summary>
        public string EventBusName { get; init; } = "default";

        /// <summary>The "source" field stamped on published events (e.g. "com.mycompany.orders").</summary>
        public string Source { get; init; } = "application";
    }

    /// <summary>
    /// Publishes integration events to Amazon EventBridge. The event's CLR type name
    /// becomes the detail-type, so EventBridge rules can pattern-match on it.
    /// Subscriptions are infrastructure (EventBridge rules + targets), not runtime
    /// concerns, so this class only implements the publish side.
    /// </summary>
    public class EventBridgeEventBus : IIntegrationEventPublisher
    {
        private readonly IAmazonEventBridge _eventBridgeClient;
        private readonly EventBridgeOptions _options;

        public EventBridgeEventBus(IAmazonEventBridge eventBridgeClient, EventBridgeOptions options)
        {
            _eventBridgeClient = eventBridgeClient;
            _options = options;
        }

        public async Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        {
            var entry = new PutEventsRequestEntry
            {
                EventBusName = _options.EventBusName,
                Source = _options.Source,
                DetailType = integrationEvent.GetType().Name,
                // Serialize as the concrete type so derived-event properties are included.
                Detail = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType())
            };

            var response = await _eventBridgeClient.PutEventsAsync(new PutEventsRequest
            {
                Entries = new List<PutEventsRequestEntry> { entry }
            }, cancellationToken);

            if (response.FailedEntryCount > 0)
            {
                var failure = response.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.ErrorCode));
                throw new InvalidOperationException(
                    $"EventBridge rejected the event: {failure?.ErrorCode} - {failure?.ErrorMessage}");
            }
        }
    }
}
