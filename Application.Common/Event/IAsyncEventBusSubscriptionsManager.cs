using Domain.Common.Event;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Common.Event
{
    public interface IAsyncEventBusSubscriptionsManager
    {
        [SuppressMessage("csharpsquid", "S3906", Justification = "Using string directly as a lightweight approach.")]
        event EventHandler<string> OnEventRemoved;

        Task<bool> IsEmptyAsync(CancellationToken cancellationToken = default);

        Task ClearAsync(CancellationToken cancellationToken = default);

        Task AddDynamicSubscriptionAsync<TH>(string eventName, CancellationToken cancellationToken = default) where TH : IDynamicIntegrationEventHandler;

        Task AddDynamicSubscriptionAsync(string eventName, Type type, CancellationToken cancellationToken = default);

        Task AddSubscriptionAsync<T, TH>(CancellationToken cancellationToken = default)
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>;

        Task RemoveDynamicSubscriptionAsync<TH>(string eventName, CancellationToken cancellationToken = default) where TH : IDynamicIntegrationEventHandler;

        Task RemoveDynamicSubscriptionAsync(string eventName, Type type, CancellationToken cancellationToken = default);

        Task RemoveSubscriptionAsync<T, TH>(CancellationToken cancellationToken = default)
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>;

        Task<IEnumerable<Subscription>> GetHandlersForEventAsync<T>(CancellationToken cancellationToken = default) where T : IntegrationEvent;

        Task<IEnumerable<Subscription>> GetHandlersForEventAsync(string eventName, CancellationToken cancellationToken = default);

        Task<bool> HasSubscriptionsForEventAsync<T>(CancellationToken cancellationToken = default) where T : IntegrationEvent;

        Task<bool> HasSubscriptionsForEventAsync(string eventName, CancellationToken cancellationToken = default);

        Task<Type?> GetEventTypeByNameAsync(string eventName, CancellationToken cancellationToken = default);
    }
}
