using AwsShowcase.Core.Abstractions;
using AwsShowcase.Core.Caching;
using AwsShowcase.Core.Dtos;
using AwsShowcase.Entity;
using Domain.Common.Repositories;
using MediatR;

namespace AwsShowcase.Core.Orders;

// ---------------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------------

public record CreateOrderCommand(string CustomerEmail, string ProductName, int Quantity, double Price, List<string>? Tags = null)
    : IRequest<OrderDto>, IRetryableRequest, ICacheInvalidatingRequest
{
    public IEnumerable<string> CacheKeysToInvalidate => new[] { CacheKeys.OrdersByCustomer(CustomerEmail) };
}

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orders;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;

    public CreateOrderCommandHandler(IOrderRepository orders, IIntegrationEventOutbox outbox, IUnitOfWork unitOfWork)
    {
        _orders = orders;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
    }

    public async Task<OrderDto> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var order = new Order
        {
            CustomerEmail = request.CustomerEmail,
            ProductName = request.ProductName,
            Quantity = request.Quantity,
            Price = request.Price,
            Tags = request.Tags ?? new List<string>(),
            Status = OrderStatus.Pending
        };

        _orders.Add(order); // generates the id and starts tracking

        // Staged in the SAME atomic save as the order itself (transactional outbox).
        _outbox.Enqueue(new OrderCreatedIntegrationEvent(order.Id, order.CustomerEmail, order.ProductName));

        await _unitOfWork.SaveEntitiesAsync(cancellationToken);

        return OrderDto.FromEntity(order);
    }
}

// ---------------------------------------------------------------------------
// Update status
// ---------------------------------------------------------------------------

public record UpdateOrderStatusCommand(string OrderId, OrderStatus NewStatus)
    : IRequest<OrderDto?>, IRetryableRequest, ICacheInvalidatingRequest
{
    public IEnumerable<string> CacheKeysToInvalidate => new[] { CacheKeys.OrderById(OrderId) };
}

public class UpdateOrderStatusCommandHandler : IRequestHandler<UpdateOrderStatusCommand, OrderDto?>
{
    private readonly IOrderRepository _orders;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateOrderStatusCommandHandler(IOrderRepository orders, IUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    public async Task<OrderDto?> Handle(UpdateOrderStatusCommand request, CancellationToken cancellationToken)
    {
        var order = await _orders.GetAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.ChangeStatus(request.NewStatus);
        _orders.Update(order);
        await _unitOfWork.SaveEntitiesAsync(cancellationToken);

        return OrderDto.FromEntity(order);
    }
}

// ---------------------------------------------------------------------------
// Delete
// ---------------------------------------------------------------------------

public record DeleteOrderCommand(string OrderId) : IRequest<bool>, ICacheInvalidatingRequest
{
    public IEnumerable<string> CacheKeysToInvalidate => new[] { CacheKeys.OrderById(OrderId) };
}

public class DeleteOrderCommandHandler : IRequestHandler<DeleteOrderCommand, bool>
{
    private readonly IOrderRepository _orders;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteOrderCommandHandler(IOrderRepository orders, IUnitOfWork unitOfWork)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> Handle(DeleteOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orders.GetAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        _orders.Remove(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
