using Application.Common.Event;
using Domain.Common.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Common.AWS.Eventbus
{
    public interface IEventBus : IDisposable
    {
        Task PublishAsync(IntegrationEvent integrationEvent);

        Task SubscribeAsync<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>;

        Task SubscribeDynamicAsync<TH>(string eventName)
            where TH : IDynamicIntegrationEventHandler;

        Task SubscribeDynamicAsync(string eventName, Type type);

        Task UnsubscribeDynamicAsync<TH>(string eventName)
            where TH : IDynamicIntegrationEventHandler;

        Task UnsubscribeDynamicAsync(string eventName, Type type);

        Task UnsubscribeAsync<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>;
    }
}
