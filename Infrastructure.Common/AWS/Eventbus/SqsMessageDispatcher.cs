using Application.Common.Event;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Common.AWS.Eventbus
{
    /// <summary>
    /// Delivers a raw SQS message to the integration-event handlers registered in the
    /// subscriptions manager. This is the consumption half of the event bus - hosted by
    /// the QueueEventDispatcher Lambda (or any SQS poller): unwraps the SNS envelope,
    /// resolves the event type and handlers by the "Subject" attribute stamped at
    /// publish time, deserializes and invokes every typed and dynamic handler.
    /// </summary>
    public interface ISqsMessageDispatcher
    {
        /// <summary>
        /// Dispatches one SQS message. When <paramref name="eventNameHint"/> is provided
        /// (raw message delivery - the event name arrives as an SQS "Subject" message
        /// attribute and the body is the raw event JSON) it is used directly; otherwise
        /// the body is parsed as an SNS notification envelope. Returns true when at least
        /// one handler ran.
        /// </summary>
        Task<bool> DispatchAsync(string sqsMessageBody, string? eventNameHint = null, CancellationToken cancellationToken = default);
    }

    public class SqsMessageDispatcher : ISqsMessageDispatcher
    {
        private readonly IAsyncEventBusSubscriptionsManager _subscriptions;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SqsMessageDispatcher> _logger;

        public SqsMessageDispatcher(IAsyncEventBusSubscriptionsManager subscriptions,
            IServiceProvider serviceProvider, ILogger<SqsMessageDispatcher> logger)
        {
            _subscriptions = subscriptions;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task<bool> DispatchAsync(string sqsMessageBody, string? eventNameHint = null, CancellationToken cancellationToken = default)
        {
            string eventName;
            string payload;

            if (!string.IsNullOrEmpty(eventNameHint))
            {
                // Raw message delivery: the body IS the event JSON, the name came from
                // the SQS "Subject" message attribute.
                eventName = eventNameHint;
                payload = sqsMessageBody;
            }
            else if (!TryUnwrapSnsEnvelope(sqsMessageBody, out eventName, out payload))
            {
                _logger.LogWarning("SQS message has no Subject attribute and is not an SNS envelope; cannot route it to handlers.");
                return false;
            }

            var handlers = (await _subscriptions.GetHandlersForEventAsync(eventName, cancellationToken)).ToList();
            if (handlers.Count == 0)
            {
                _logger.LogWarning("No handlers subscribed for event {EventName}; message skipped.", eventName);
                return false;
            }

            var eventType = await _subscriptions.GetEventTypeByNameAsync(eventName, cancellationToken);
            var invoked = 0;

            foreach (var subscription in handlers)
            {
                var handlerType = subscription.ResolveHandlerType();
                if (handlerType is null)
                {
                    _logger.LogError("Handler type '{HandlerType}' for {EventName} could not be loaded.", subscription.HandlerType, eventName);
                    continue;
                }

                var handler = _serviceProvider.GetService(handlerType)
                    ?? ActivatorUtilities.CreateInstance(_serviceProvider, handlerType);

                if (subscription.IsDynamic)
                {
                    if (handler is IDynamicIntegrationEventHandler dynamicHandler)
                    {
                        using var payloadDocument = JsonDocument.Parse(payload);
                        await dynamicHandler.Handle(payloadDocument, cancellationToken);
                        invoked++;
                    }
                }
                else
                {
                    if (eventType is null)
                    {
                        _logger.LogError("Event type for {EventName} is not registered; typed handler {Handler} skipped.", eventName, handlerType.Name);
                        continue;
                    }

                    var integrationEvent = JsonSerializer.Deserialize(payload, eventType)
                        ?? throw new InvalidOperationException($"Payload for {eventName} deserialized to null.");

                    var handlerInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
                    var handleMethod = handlerInterface.GetMethod(nameof(IDynamicIntegrationEventHandler.Handle))!;
                    await (Task)handleMethod.Invoke(handler, new[] { integrationEvent, (object)cancellationToken })!;
                    invoked++;
                }
            }

            _logger.LogInformation("Dispatched {EventName} to {HandlerCount} handler(s).", eventName, invoked);
            return invoked > 0;
        }

        /// <summary>
        /// SNS delivers to SQS as an envelope: {"Type":"Notification","Message":"...json...",
        /// "MessageAttributes":{"Subject":{"Value":"OrderCreatedIntegrationEvent"}}}. The
        /// Subject attribute (stamped by the publisher) is the routing key.
        /// </summary>
        private static bool TryUnwrapSnsEnvelope(string body, out string eventName, out string payload)
        {
            eventName = string.Empty;
            payload = string.Empty;

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("Message", out var message) ||
                    !root.TryGetProperty("MessageAttributes", out var attributes) ||
                    !attributes.TryGetProperty("Subject", out var subject) ||
                    !subject.TryGetProperty("Value", out var subjectValue))
                {
                    return false;
                }

                eventName = subjectValue.GetString() ?? string.Empty;
                payload = message.GetString() ?? string.Empty;
                return eventName.Length > 0 && payload.Length > 0;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
