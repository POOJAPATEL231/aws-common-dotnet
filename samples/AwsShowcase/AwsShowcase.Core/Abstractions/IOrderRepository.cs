using AwsShowcase.Entity;
using Domain.Common.Event;
using Persistence.Common.Repositories;

namespace AwsShowcase.Core.Abstractions;

/// <summary>
/// Order persistence port. Inherits the full document-repository surface;
/// the DynamoDB implementation lives in AwsShowcase.Integration.
/// </summary>
public interface IOrderRepository : IDocRepository<Order>
{
}

/// <summary>
/// Port for staging integration events in the transactional outbox - they are
/// written atomically with the unit of work and published afterwards by the
/// outbox dispatcher. Kept as an abstraction so handlers stay unit-testable.
/// </summary>
public interface IIntegrationEventOutbox
{
    void Enqueue(IntegrationEvent integrationEvent);
}
