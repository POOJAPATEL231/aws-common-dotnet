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
}
