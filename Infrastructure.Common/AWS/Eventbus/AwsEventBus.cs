using Application.Common.Event;
using Domain.Common.Event;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS.Eventbus
{
    public class AwsEventBus : IEventBus
    {
        private readonly IEventBusPersisterConnection _eventBusPersisterConnection;
        private readonly ILogger<AwsEventBus> _logger;
        private readonly IAsyncEventBusSubscriptionsManager _subsManager;

        public AwsEventBus(IEventBusPersisterConnection eventBusPersisterConnection, ILogger<AwsEventBus> logger, IAsyncEventBusSubscriptionsManager subsManager)
        {
            _eventBusPersisterConnection = eventBusPersisterConnection ?? throw new ArgumentNullException(nameof(eventBusPersisterConnection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _subsManager = subsManager;
        }

        public async Task PublishAsync(IntegrationEvent integrationEvent)
        {
            var eventName = integrationEvent.GetType().Name;
            var jsonMessage = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType());
            await _eventBusPersisterConnection.PublishAsync(jsonMessage, eventName);
        }

        public async Task SubscribeAsync<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            var eventName = typeof(T).Name;
            var containsKey = await _subsManager.HasSubscriptionsForEventAsync<T>();
            if (!containsKey)
            {
                await SubscribeToTopicAsync(eventName);
            }
            _logger.LogDebug("Subscribing to event {EventName} with {EventHandler}", eventName, typeof(TH).Name);
            await _subsManager.AddSubscriptionAsync<T, TH>();
        }

        public async Task SubscribeDynamicAsync<TH>(string eventName)
            where TH : IDynamicIntegrationEventHandler
        {
            var containsKey = await _subsManager.HasSubscriptionsForEventAsync(eventName);
            if (!containsKey)
            {
                await SubscribeToTopicAsync(eventName);
            }
            _logger.LogDebug("Subscribing to dynamic event {EventName} with {EventHandler}",
                eventName, typeof(TH).Name);
            await _subsManager.AddDynamicSubscriptionAsync<TH>(eventName);
        }

        public async Task SubscribeDynamicAsync(string eventName, Type type)
        {
            var containsKey = await _subsManager.HasSubscriptionsForEventAsync(eventName);
            if (!containsKey)
            {
                await SubscribeToTopicAsync(eventName);
            }
            _logger.LogDebug("Subscribing to dynamic event {EventName} with {EventHandler}",
                eventName, type.Name);
            await _subsManager.AddDynamicSubscriptionAsync(eventName, type);
        }

        public async Task UnsubscribeAsync<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            var eventName = typeof(T).Name;
            await UnSubscribeFromTopicAsync(eventName);
            _logger.LogDebug("Unsubscribing from event {EventName}", eventName);
            await _subsManager.RemoveSubscriptionAsync<T, TH>();
        }

        public async Task UnsubscribeDynamicAsync<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
        {
            await UnSubscribeFromTopicAsync(eventName);
            _logger.LogDebug("Unsubscribing from dynamic event {EventName}", eventName);
            await _subsManager.RemoveDynamicSubscriptionAsync<TH>(eventName);
        }

        public async Task UnsubscribeDynamicAsync(string eventName, Type type)
        {
            await UnSubscribeFromTopicAsync(eventName);
            _logger.LogDebug("Unsubscribing from dynamic event {EventName}", eventName);
            await _subsManager.RemoveDynamicSubscriptionAsync(eventName, type);
        }

        private async Task SubscribeToTopicAsync(string eventName)
        {
            var topicName = _eventBusPersisterConnection.GetTopicName(eventName) ?? eventName;
            try
            {
                await _eventBusPersisterConnection.GetOrCreateTopicWithSetupAsync(topicName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error has occurred while subscribing to topic: {Topic}  with event: {Event}.",
                    topicName, eventName);
            }
        }

        private async Task UnSubscribeFromTopicAsync(string eventName)
        {
            var topicName = _eventBusPersisterConnection.GetTopicName(eventName);
            try
            {
                await _eventBusPersisterConnection.UnsubscribeAsync(topicName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The messaging entity {EventName} Could not be found.", eventName);
            }
        }

        #region IDisposable Methods

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {

            //_changeTracker.Clear();
        }

        #endregion
    }
}
