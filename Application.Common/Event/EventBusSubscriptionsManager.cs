using Domain.Common.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Event
{
    public class EventBusSubscriptionsManager : IAsyncEventBusSubscriptionsManager
    {
        private readonly ICache _cache;

        public event EventHandler<string>? OnEventRemoved;

        public EventBusSubscriptionsManager(ICache cache)
        {
            _cache = cache;
        }

        public async Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default)
        {
            var handlersKeys = await _cache.GetAsync<List<string>>(Constant.EventTypesKey);
            return handlersKeys == null || handlersKeys.Count <= 0;
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            var eventTypes = await _cache.GetAsync<List<string>>(Constant.EventTypesKey);
            if (eventTypes != null)
            {
                foreach (var eventType in eventTypes)
                {
                    await _cache.RemoveAsync($"{Constant.HandlersKeyPrefix}{eventType}");
                }
            }

            await _cache.RemoveAsync(Constant.EventTypesKey);
        }

        public async Task AddDynamicSubscriptionAsync<TH>(string eventName, CancellationToken cancellationToken = default)
            where TH : IDynamicIntegrationEventHandler
        {
            await DoAddSubscriptionAsync(typeof(TH), eventName, isDynamic: true);
        }

        public async Task AddDynamicSubscriptionAsync(string eventName, Type type, CancellationToken cancellationToken = default)
        {
            await DoAddSubscriptionAsync(type, eventName, isDynamic: true);
        }

        public async Task AddSubscriptionAsync<T, TH>(CancellationToken cancellationToken = default)
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            var eventName = GetEventKey<T>();
            await DoAddSubscriptionAsync(typeof(TH), eventName, isDynamic: false);

            // Ensure the event type is tracked
            var eventType = typeof(T).AssemblyQualifiedName;
            if (!string.IsNullOrWhiteSpace(eventType))
            {
                var eventTypes = await _cache.GetAsync<List<string>>(Constant.EventTypesKey) ?? new List<string>();
                if (!eventTypes.Contains(eventType))
                {
                    eventTypes.Add(eventType);
                    await _cache.SetAsync(Constant.EventTypesKey, eventTypes);
                }
            }
        }

        private async Task DoAddSubscriptionAsync(Type handlerType, string eventName, bool isDynamic)
        {
            var handlersKey = $"{Constant.HandlersKeyPrefix}{eventName}";
            var handlers = await _cache.GetAsync<List<Subscription>>(handlersKey) ?? new List<Subscription>();

            if (!handlers.Exists(s => s.HandlerType == handlerType.AssemblyQualifiedName))
            {
                handlers.Add(isDynamic ? Subscription.Dynamic(handlerType) : Subscription.Typed(handlerType));
                await _cache.SetAsync(handlersKey, handlers);
            }
        }

        public async Task RemoveDynamicSubscriptionAsync<TH>(string eventName, CancellationToken cancellationToken = default)
            where TH : IDynamicIntegrationEventHandler
        {
            await RemoveSubscriptionAsync(eventName, typeof(TH));
        }

        public async Task RemoveDynamicSubscriptionAsync(string eventName, Type type, CancellationToken cancellationToken = default)
        {
            await RemoveSubscriptionAsync(eventName, type);
        }

        public async Task RemoveSubscriptionAsync<T, TH>(CancellationToken cancellationToken = default)
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            var eventName = GetEventKey<T>();
            await RemoveSubscriptionAsync(eventName, typeof(TH));
        }

        private async Task RemoveSubscriptionAsync(string eventName, Type handlerType)
        {
            var handlersKey = $"{Constant.HandlersKeyPrefix}{eventName}";
            var handlers = await _cache.GetAsync<List<Subscription>>(handlersKey);

            if (handlers == null)
            {
                return;
            }

            var handlerToRemove = handlers.SingleOrDefault(s => s.HandlerType == handlerType.AssemblyQualifiedName);
            if (handlerToRemove != null)
            {
                handlers.Remove(handlerToRemove);
                if (handlers.Count <= 0)
                {
                    await _cache.RemoveAsync(handlersKey);

                    // Remove event type tracking if no handlers remain.
                    // The list stores assembly-qualified names, so match by the resolved type's simple name.
                    var eventTypes = await _cache.GetAsync<List<string>>(Constant.EventTypesKey) ?? new List<string>();

                    eventTypes.RemoveAll(e => Type.GetType(e)?.Name == eventName);
                    await _cache.SetAsync(Constant.EventTypesKey, eventTypes);

                    RaiseOnEventRemoved(eventName);
                }
                else
                {
                    await _cache.SetAsync(handlersKey, handlers);
                }
            }
        }

        public async Task<IEnumerable<Subscription>> GetHandlersForEventAsync<T>(CancellationToken cancellationToken = default) where T : IntegrationEvent
        {
            var key = GetEventKey<T>();
            return await GetHandlersForEventAsync(key, cancellationToken);
        }

        public async Task<IEnumerable<Subscription>> GetHandlersForEventAsync(string eventName, CancellationToken cancellationToken = default)
        {
            var handlersKey = $"{Constant.HandlersKeyPrefix}{eventName}";
            return await _cache.GetAsync<List<Subscription>>(handlersKey) ?? Enumerable.Empty<Subscription>();
        }

        private void RaiseOnEventRemoved(string eventName)
        {
            var handler = OnEventRemoved;
            handler?.Invoke(this, eventName);
        }

        public async Task<bool> HasSubscriptionsForEventAsync<T>(CancellationToken cancellationToken = default) where T : IntegrationEvent
        {
            var key = GetEventKey<T>();
            return await HasSubscriptionsForEventAsync(key, cancellationToken);
        }

        public async Task<bool> HasSubscriptionsForEventAsync(string eventName, CancellationToken cancellationToken = default)
        {
            var handlersKey = $"{Constant.HandlersKeyPrefix}{eventName}";
            var handlers = await _cache.GetAsync<List<Subscription>>(handlersKey);
            return handlers != null && handlers.Count > 0;
        }

        public async Task<Type?> GetEventTypeByNameAsync(string eventName, CancellationToken cancellationToken = default)
        {
            var eventTypes = await _cache.GetAsync<List<string>>(Constant.EventTypesKey);
            if (eventTypes is { Count: > 0 })
            {
                var types = eventTypes.Select(e => Type.GetType(e)).ToList();
                return types.SingleOrDefault(t => t?.Name == eventName);
            }
            return null;
        }

        public static string GetEventKey<T>()
        {
            return typeof(T).Name;
        }
    }
}
