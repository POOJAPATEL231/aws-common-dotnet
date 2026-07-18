using AwsShowcase.Core.Abstractions;
using AwsShowcase.Core.Caching;
using AwsShowcase.Core.Dtos;
using MediatR;

namespace AwsShowcase.Core.Orders;

// ---------------------------------------------------------------------------
// Get by id (cached read-through)
// ---------------------------------------------------------------------------

public record GetOrderByIdQuery(string OrderId) : IRequest<OrderDto?>, ICacheableQuery
{
    public string CacheKey => CacheKeys.OrderById(OrderId);
    public TimeSpan Expiration => TimeSpan.FromMinutes(5);
}

public class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    private readonly IOrderRepository _orders;

    public GetOrderByIdQueryHandler(IOrderRepository orders)
    {
        _orders = orders;
    }

    public async Task<OrderDto?> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await _orders.GetAsync(request.OrderId, cancellationToken);
        return order is null ? null : OrderDto.FromEntity(order);
    }
}

// ---------------------------------------------------------------------------
// Get by customer (cached; runs as a GSI Query thanks to the CustomerEmail index)
// ---------------------------------------------------------------------------

public record GetOrdersByCustomerQuery(string CustomerEmail) : IRequest<List<OrderDto>>, ICacheableQuery
{
    public string CacheKey => CacheKeys.OrdersByCustomer(CustomerEmail);
    public TimeSpan Expiration => TimeSpan.FromMinutes(2);
}

public class GetOrdersByCustomerQueryHandler : IRequestHandler<GetOrdersByCustomerQuery, List<OrderDto>>
{
    private readonly IOrderRepository _orders;

    public GetOrdersByCustomerQueryHandler(IOrderRepository orders)
    {
        _orders = orders;
    }

    public async Task<List<OrderDto>> Handle(GetOrdersByCustomerQuery request, CancellationToken cancellationToken)
    {
        var orders = await _orders.GetAsync(o => o.CustomerEmail == request.CustomerEmail, cancellationToken);
        return orders.Select(OrderDto.FromEntity).ToList();
    }
}

// ---------------------------------------------------------------------------
// Paged listing (not cached - inherently volatile)
// ---------------------------------------------------------------------------

public record GetPagedOrdersQuery(int Page = 1, int PageSize = 10) : IRequest<PagedResultDto<OrderDto>>;

public class GetPagedOrdersQueryHandler : IRequestHandler<GetPagedOrdersQuery, PagedResultDto<OrderDto>>
{
    private readonly IOrderRepository _orders;

    public GetPagedOrdersQueryHandler(IOrderRepository orders)
    {
        _orders = orders;
    }

    public async Task<PagedResultDto<OrderDto>> Handle(GetPagedOrdersQuery request, CancellationToken cancellationToken)
    {
        var paged = await _orders.GetPagedAsync(request.Page, request.PageSize,
            o => o.CreateDateTimeUtc, sortDescending: true, cancellationToken);

        return new PagedResultDto<OrderDto>(
            paged.Items.Select(OrderDto.FromEntity).ToList(),
            paged.Page, paged.PageSize, paged.TotalRecords, paged.TotalPages);
    }
}
