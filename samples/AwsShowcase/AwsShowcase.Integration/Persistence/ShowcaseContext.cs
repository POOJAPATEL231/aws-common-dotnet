using AwsShowcase.Core.Abstractions;
using AwsShowcase.Entity;
using Domain.Common.Dates;
using Domain.Common.Event;
using Domain.Common.Identity;
using MediatR;
using Persistence.Common.AWS;
using Persistence.Common.AWS.Outbox;
using DynamoRepositories = Persistence.Common.AWS.Repositories;

namespace AwsShowcase.Integration.Persistence;

/// <summary>EF-style DynamoDB context - one IDynamoDbSet property per aggregate.</summary>
public class ShowcaseContext : BaseDynamoDbContext
{
    public ShowcaseContext(IServiceProvider serviceProvider, ICurrentUser currentUser, IDateTime dateTime, IMediator mediator)
        : base(serviceProvider, currentUser, dateTime, mediator)
    {
    }

    public IDynamoDbSet<Order> Orders => Set<Order>();
}

/// <summary>DynamoDB implementation of the order port.</summary>
public class OrderRepository : DynamoRepositories.DynamoDbRepository<Order>, IOrderRepository
{
    public OrderRepository(ShowcaseContext context) : base(context)
    {
    }
}

/// <summary>
/// Bridges the Core outbox port to the library's transactional outbox: enqueued
/// events are written atomically with the unit of work's other changes.
/// </summary>
public class TransactionalOutbox : IIntegrationEventOutbox
{
    private readonly ShowcaseContext _context;

    public TransactionalOutbox(ShowcaseContext context)
    {
        _context = context;
    }

    public void Enqueue(IntegrationEvent integrationEvent) => _context.AddOutboxMessage(integrationEvent);
}
