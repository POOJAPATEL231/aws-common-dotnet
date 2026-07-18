using Domain.Common.Event;
using System.Text.Json;

namespace Application.Common.Event
{
    public interface IDynamicIntegrationEventHandler
    {
        Task Handle(JsonDocument eventData, CancellationToken cancellationToken = default);
    }
    public interface IIntegrationEventHandler<in TIntegrationEvent>
            where TIntegrationEvent : IntegrationEvent
    {
        Task Handle(TIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
    }
    public interface IIntegrationEventService
    {
        Task PublishEventsThroughEventBusAsync(CancellationToken cancellationToken = default);

        Task PublishEventsThroughEventBusAsync(Guid transactionId,
            CancellationToken cancellationToken = default);
        Task AddAndSaveEventAsync(IntegrationEvent integrationEvent,
            CancellationToken cancellationToken = default);
    }
}
