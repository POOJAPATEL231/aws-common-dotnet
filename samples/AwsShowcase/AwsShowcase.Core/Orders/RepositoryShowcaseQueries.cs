using AwsShowcase.Core.Abstractions;
using AwsShowcase.Core.Dtos;
using AwsShowcase.Entity;
using MediatR;

namespace AwsShowcase.Core.Orders;

// ---------------------------------------------------------------------------
// Use case: exact key lookup - repository.FindAsync(partitionKey, id).
// Checks the change tracker first, then DynamoDB GetItem.
// ---------------------------------------------------------------------------

public record FindOrderByKeysQuery(string PartitionKey, string OrderId) : IRequest<OrderDto?>;

public class FindOrderByKeysQueryHandler : IRequestHandler<FindOrderByKeysQuery, OrderDto?>
{
    private readonly IOrderRepository _orders;

    public FindOrderByKeysQueryHandler(IOrderRepository orders) => _orders = orders;

    public async Task<OrderDto?> Handle(FindOrderByKeysQuery request, CancellationToken cancellationToken)
    {
        var order = await _orders.FindAsync(request.PartitionKey, request.OrderId);
        return order is null ? null : OrderDto.FromEntity(order);
    }
}

// ---------------------------------------------------------------------------
// Use case: full listing - repository.GetAllAsync() and its sorted overload
// GetAllAsync(sortKeySelector, sortDescending).
// ---------------------------------------------------------------------------

public record GetAllOrdersQuery(bool SortByNewestFirst = false) : IRequest<List<OrderDto>>;

public class GetAllOrdersQueryHandler : IRequestHandler<GetAllOrdersQuery, List<OrderDto>>
{
    private readonly IOrderRepository _orders;

    public GetAllOrdersQueryHandler(IOrderRepository orders) => _orders = orders;

    public async Task<List<OrderDto>> Handle(GetAllOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = request.SortByNewestFirst
            ? await _orders.GetAllAsync(o => o.CreateDateTimeUtc, sortDescending: true, cancellationToken)
            : await _orders.GetAllAsync(cancellationToken);

        return orders.Select(OrderDto.FromEntity).ToList();
    }
}

// ---------------------------------------------------------------------------
// Use case: filtered + sorted search - repository.GetAsync(predicate, sortKey, desc).
// ---------------------------------------------------------------------------

public record SearchOrdersQuery(int MinQuantity, bool MostExpensiveFirst = true) : IRequest<List<OrderDto>>;

public class SearchOrdersQueryHandler : IRequestHandler<SearchOrdersQuery, List<OrderDto>>
{
    private readonly IOrderRepository _orders;

    public SearchOrdersQueryHandler(IOrderRepository orders) => _orders = orders;

    public async Task<List<OrderDto>> Handle(SearchOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = await _orders.GetAsync(
            o => o.Quantity >= request.MinQuantity,
            o => o.Price,
            request.MostExpensiveFirst,
            cancellationToken);

        return orders.Select(OrderDto.FromEntity).ToList();
    }
}

// ---------------------------------------------------------------------------
// Use case: filtered paging - repository.GetPagedAsync(page, size, predicate, sort, desc).
// ---------------------------------------------------------------------------

public record GetPagedOrdersByStatusQuery(OrderStatus Status, int Page = 1, int PageSize = 10)
    : IRequest<PagedResultDto<OrderDto>>;

public class GetPagedOrdersByStatusQueryHandler : IRequestHandler<GetPagedOrdersByStatusQuery, PagedResultDto<OrderDto>>
{
    private readonly IOrderRepository _orders;

    public GetPagedOrdersByStatusQueryHandler(IOrderRepository orders) => _orders = orders;

    public async Task<PagedResultDto<OrderDto>> Handle(GetPagedOrdersByStatusQuery request, CancellationToken cancellationToken)
    {
        var paged = await _orders.GetPagedAsync(request.Page, request.PageSize,
            o => o.Status == request.Status,
            o => o.CreateDateTimeUtc,
            sortDescending: true,
            cancellationToken);

        return new PagedResultDto<OrderDto>(
            paged.Items.Select(OrderDto.FromEntity).ToList(),
            paged.Page, paged.PageSize, paged.TotalRecords, paged.TotalPages);
    }
}

// ---------------------------------------------------------------------------
// Use case: newest order of a customer - repository.FirstOrDefaultAsync in both
// the plain and the sorted overloads.
// ---------------------------------------------------------------------------

public record GetLatestOrderQuery(string CustomerEmail, bool UseSortedOverload = true) : IRequest<OrderDto?>;

public class GetLatestOrderQueryHandler : IRequestHandler<GetLatestOrderQuery, OrderDto?>
{
    private readonly IOrderRepository _orders;

    public GetLatestOrderQueryHandler(IOrderRepository orders) => _orders = orders;

    public async Task<OrderDto?> Handle(GetLatestOrderQuery request, CancellationToken cancellationToken)
    {
        var order = request.UseSortedOverload
            ? await _orders.FirstOrDefaultAsync(
                o => o.CustomerEmail == request.CustomerEmail,
                o => o.CreateDateTimeUtc, sortDescending: true, cancellationToken)
            : await _orders.FirstOrDefaultAsync(
                o => o.CustomerEmail == request.CustomerEmail, cancellationToken);

        return order is null ? null : OrderDto.FromEntity(order);
    }
}

// ---------------------------------------------------------------------------
// Use case: existence probe - repository.AnyAsync(predicate) (COUNT under the hood,
// no items materialized).
// ---------------------------------------------------------------------------

public record CustomerHasOrdersQuery(string CustomerEmail) : IRequest<bool>;

public class CustomerHasOrdersQueryHandler : IRequestHandler<CustomerHasOrdersQuery, bool>
{
    private readonly IOrderRepository _orders;

    public CustomerHasOrdersQueryHandler(IOrderRepository orders) => _orders = orders;

    public Task<bool> Handle(CustomerHasOrdersQuery request, CancellationToken cancellationToken)
        => _orders.AnyAsync(o => o.CustomerEmail == request.CustomerEmail, cancellationToken);
}

// ---------------------------------------------------------------------------
// Use case: partition-scoped reads - the IDocRepository extras
// GetAllAsync(partitionKey) and GetAsync(partitionKey, predicate): a partition-key
// Query instead of a Scan even without a GSI.
// ---------------------------------------------------------------------------

public record GetOrdersInPartitionQuery(string PartitionKey = "order", int? MinQuantity = null) : IRequest<List<OrderDto>>;

public class GetOrdersInPartitionQueryHandler : IRequestHandler<GetOrdersInPartitionQuery, List<OrderDto>>
{
    private readonly IOrderRepository _orders;

    public GetOrdersInPartitionQueryHandler(IOrderRepository orders) => _orders = orders;

    public async Task<List<OrderDto>> Handle(GetOrdersInPartitionQuery request, CancellationToken cancellationToken)
    {
        var orders = request.MinQuantity is null
            ? await _orders.GetAllAsync(request.PartitionKey, cancellationToken)
            : await _orders.GetAsync(request.PartitionKey, o => o.Quantity >= request.MinQuantity.Value, cancellationToken);

        return orders.Select(OrderDto.FromEntity).ToList();
    }
}

// ---------------------------------------------------------------------------
// Use case: read-only reporting - repository.AsNoTracking() + Include (both
// overloads; Include is a documented no-op for DynamoDB, AsNoTracking skips
// change tracking for cheap read paths).
// ---------------------------------------------------------------------------

public record GetOrdersReportQuery : IRequest<List<OrderDto>>;

public class GetOrdersReportQueryHandler : IRequestHandler<GetOrdersReportQuery, List<OrderDto>>
{
    private readonly IOrderRepository _orders;

    public GetOrdersReportQueryHandler(IOrderRepository orders) => _orders = orders;

    public async Task<List<OrderDto>> Handle(GetOrdersReportQuery request, CancellationToken cancellationToken)
    {
        var orders = await _orders
            .AsNoTracking()
            .Include(o => o.Tags)
            .Include(nameof(Order.Tags))
            .GetAllAsync(cancellationToken);

        return orders.Select(OrderDto.FromEntity).ToList();
    }
}
