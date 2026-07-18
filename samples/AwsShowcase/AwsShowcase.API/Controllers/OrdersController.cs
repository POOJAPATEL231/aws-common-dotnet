using AwsShowcase.Core.Dtos;
using AwsShowcase.Core.Orders;
using AwsShowcase.Entity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AwsShowcase.API.Controllers;

/// <summary>
/// CQRS over the DynamoDB EF-style layer. Controllers stay thin: every action is
/// one IMediator.Send - caching, retries and logging happen in the pipeline.
/// </summary>
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Creates an order + stages an OrderCreated event in the transactional outbox.</summary>
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create([FromBody] CreateOrderCommand command, CancellationToken ct)
    {
        var created = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Cached read-through: first call hits DynamoDB, subsequent calls hit the cache.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<OrderDto>> GetById(string id, CancellationToken ct)
    {
        var order = await _mediator.Send(new GetOrderByIdQuery(id), ct);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Runs as a GSI Query (CustomerEmail index) - cached for 2 minutes.</summary>
    [HttpGet("by-customer/{email}")]
    public async Task<ActionResult<List<OrderDto>>> GetByCustomer(string email, CancellationToken ct)
        => Ok(await _mediator.Send(new GetOrdersByCustomerQuery(email), ct));

    /// <summary>Paged listing sorted by creation date.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResultDto<OrderDto>>> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => Ok(await _mediator.Send(new GetPagedOrdersQuery(page, pageSize), ct));

    /// <summary>Updates the status; the pipeline invalidates the order's cache entry afterwards.</summary>
    [HttpPut("{id}/status")]
    public async Task<ActionResult<OrderDto>> UpdateStatus(string id, [FromQuery] OrderStatus status, CancellationToken ct)
    {
        var updated = await _mediator.Send(new UpdateOrderStatusCommand(id, status), ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Deletes the order and invalidates its cache entry.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
        => await _mediator.Send(new DeleteOrderCommand(id), ct) ? NoContent() : NotFound();

    // -----------------------------------------------------------------------
    // Repository showcase - one use case per remaining repository method
    // -----------------------------------------------------------------------

    /// <summary>repository.FindAsync(partitionKey, id) - exact key lookup via the change tracker/GetItem.</summary>
    [HttpGet("find")]
    public async Task<ActionResult<OrderDto>> Find([FromQuery] string partitionKey, [FromQuery] string id, CancellationToken ct)
    {
        var order = await _mediator.Send(new FindOrderByKeysQuery(partitionKey, id), ct);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>repository.GetAllAsync() / GetAllAsync(sort, desc) - full listing, optionally newest-first.</summary>
    [HttpGet("all")]
    public async Task<ActionResult<List<OrderDto>>> GetAll([FromQuery] bool newestFirst = false, CancellationToken ct = default)
        => Ok(await _mediator.Send(new GetAllOrdersQuery(newestFirst), ct));

    /// <summary>repository.GetAsync(predicate, sortKey, desc) - filtered search sorted by price.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<List<OrderDto>>> Search([FromQuery] int minQuantity = 1, [FromQuery] bool mostExpensiveFirst = true, CancellationToken ct = default)
        => Ok(await _mediator.Send(new SearchOrdersQuery(minQuantity, mostExpensiveFirst), ct));

    /// <summary>repository.GetPagedAsync(page, size, predicate, sort, desc) - filtered paging by status.</summary>
    [HttpGet("by-status/{status}")]
    public async Task<ActionResult<PagedResultDto<OrderDto>>> GetByStatus(OrderStatus status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => Ok(await _mediator.Send(new GetPagedOrdersByStatusQuery(status, page, pageSize), ct));

    /// <summary>repository.FirstOrDefaultAsync (plain + sorted overloads) - a customer's latest order.</summary>
    [HttpGet("latest/{email}")]
    public async Task<ActionResult<OrderDto>> Latest(string email, [FromQuery] bool sorted = true, CancellationToken ct = default)
    {
        var order = await _mediator.Send(new GetLatestOrderQuery(email, sorted), ct);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>repository.AnyAsync(predicate) - existence probe without materializing items.</summary>
    [HttpGet("exists/{email}")]
    public async Task<ActionResult<bool>> Exists(string email, CancellationToken ct)
        => Ok(await _mediator.Send(new CustomerHasOrdersQuery(email), ct));

    /// <summary>repository.GetAllAsync(partitionKey) / GetAsync(partitionKey, predicate) - partition Query without a GSI.</summary>
    [HttpGet("partition/{partitionKey}")]
    public async Task<ActionResult<List<OrderDto>>> ByPartition(string partitionKey, [FromQuery] int? minQuantity, CancellationToken ct)
        => Ok(await _mediator.Send(new GetOrdersInPartitionQuery(partitionKey, minQuantity), ct));

    /// <summary>repository.AsNoTracking() + Include (both overloads) - cheap read-only reporting path.</summary>
    [HttpGet("report")]
    public async Task<ActionResult<List<OrderDto>>> Report(CancellationToken ct)
        => Ok(await _mediator.Send(new GetOrdersReportQuery(), ct));

    /// <summary>repository.AddRange + one atomic SaveEntitiesAsync - bulk import with outbox events.</summary>
    [HttpPost("batch")]
    public async Task<ActionResult<List<OrderDto>>> CreateBatch([FromBody] List<OrderLineDto> lines, CancellationToken ct)
        => Ok(await _mediator.Send(new CreateOrdersBatchCommand(lines), ct));

    /// <summary>repository.RemoveRange + SaveChangesAsync - purge all of a customer's orders.</summary>
    [HttpDelete("by-customer/{email}")]
    public async Task<ActionResult<int>> DeleteByCustomer(string email, CancellationToken ct)
        => Ok(await _mediator.Send(new DeleteOrdersByCustomerCommand(email), ct));

    /// <summary>repository.Update per entity + GetModified() before the atomic save - bulk discount.</summary>
    [HttpPost("discount/{email}")]
    public async Task<ActionResult<int>> ApplyDiscount(string email, [FromQuery] double percent = 10, CancellationToken ct = default)
        => Ok(await _mediator.Send(new ApplyDiscountCommand(email, percent), ct));
}
