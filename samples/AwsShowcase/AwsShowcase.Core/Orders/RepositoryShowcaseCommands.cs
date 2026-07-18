using AwsShowcase.Core.Abstractions;
using AwsShowcase.Core.Caching;
using AwsShowcase.Core.Dtos;
using AwsShowcase.Entity;
using Domain.Common.Repositories;
using MediatR;

namespace AwsShowcase.Core.Orders;

// ---------------------------------------------------------------------------
// Use case: bulk import - repository.AddRange + one atomic SaveEntitiesAsync
// (single DynamoDB transaction; all-or-nothing).
// ---------------------------------------------------------------------------

public record OrderLineDto(string CustomerEmail, string ProductName, int Quantity, double Price);

public record CreateOrdersBatchCommand(List<OrderLineDto> Orders) : IRequest<List<OrderDto>>, IRetryableRequest;

public class CreateOrdersBatchCommandHandler : IRequestHandler<CreateOrdersBatchCommand, List<OrderDto>>
{
    private readonly IOrderRepository _orders;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;

    public CreateOrdersBatchCommandHandler(IOrderRepository orders, IIntegrationEventOutbox outbox, IUnitOfWork unitOfWork)
    {
        _orders = orders;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
    }

    public async Task<List<OrderDto>> Handle(CreateOrdersBatchCommand request, CancellationToken cancellationToken)
    {
        var entities = request.Orders.Select(line => new Order
        {
            CustomerEmail = line.CustomerEmail,
            ProductName = line.ProductName,
            Quantity = line.Quantity,
            Price = line.Price,
            Status = OrderStatus.Pending
        }).ToList();

        _orders.AddRange(entities);

        foreach (var order in entities)
        {
            _outbox.Enqueue(new OrderCreatedIntegrationEvent(order.Id, order.CustomerEmail, order.ProductName));
        }

        await _unitOfWork.SaveEntitiesAsync(cancellationToken);
        return entities.Select(OrderDto.FromEntity).ToList();
    }
}

// ---------------------------------------------------------------------------
// Use case: GDPR-style purge - repository.GetAsync(predicate) + RemoveRange +
// SaveChangesAsync; invalidates the customer's cached listing.
// ---------------------------------------------------------------------------

public record DeleteOrdersByCustomerCommand(string CustomerEmail) : IRequest<int>, ICacheInvalidatingRequest
{
    public IEnumerable<string> CacheKeysToInvalidate => new[] { CacheKeys.OrdersByCustomer(CustomerEmail) };
}

public class DeleteOrdersByCustomerCommandHandler : IRequestHandler<DeleteOrdersByCustomerCommand, int>
{
    private readonly IOrderRepository _orders;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteOrdersByCustomerCommandHandler(IOrderRepository orders, IUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    public async Task<int> Handle(DeleteOrdersByCustomerCommand request, CancellationToken cancellationToken)
    {
        var orders = await _orders.GetAsync(o => o.CustomerEmail == request.CustomerEmail, cancellationToken);
        if (orders.Count == 0)
        {
            return 0;
        }

        _orders.RemoveRange(orders);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return orders.Count;
    }
}

// ---------------------------------------------------------------------------
// Use case: bulk price adjustment - Update per entity, repository.GetModified()
// to inspect the pending change set BEFORE the save commits it atomically.
// ---------------------------------------------------------------------------

public record ApplyDiscountCommand(string CustomerEmail, double Percent) : IRequest<int>, IRetryableRequest, ICacheInvalidatingRequest
{
    public IEnumerable<string> CacheKeysToInvalidate => new[] { CacheKeys.OrdersByCustomer(CustomerEmail) };
}

public class ApplyDiscountCommandHandler : IRequestHandler<ApplyDiscountCommand, int>
{
    private readonly IOrderRepository _orders;
    private readonly IUnitOfWork _unitOfWork;

    public ApplyDiscountCommandHandler(IOrderRepository orders, IUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    public async Task<int> Handle(ApplyDiscountCommand request, CancellationToken cancellationToken)
    {
        var orders = await _orders.GetAsync(
            o => o.CustomerEmail == request.CustomerEmail && o.Status == OrderStatus.Pending, cancellationToken);

        foreach (var order in orders)
        {
            order.Price = Math.Round(order.Price * (1 - request.Percent / 100d), 2);
            _orders.Update(order);
        }

        // GetModified() surfaces the tracked change set before committing.
        var pendingChanges = _orders.GetModified().Count;

        await _unitOfWork.SaveEntitiesAsync(cancellationToken);
        return pendingChanges;
    }
}
