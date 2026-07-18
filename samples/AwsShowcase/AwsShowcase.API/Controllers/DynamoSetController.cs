using AwsShowcase.Entity;
using AwsShowcase.Integration.Persistence;
using Microsoft.AspNetCore.Mvc;
using Persistence.Common.AWS;

namespace AwsShowcase.API.Controllers;

/// <summary>
/// Direct IDynamoDbSet&lt;T&gt; surface (below the repository): immediate reads/writes
/// against DynamoDB plus the QueryExpression fluent builder. Unlike repository +
/// Unit of Work, set-level writes execute immediately (no change tracking).
/// </summary>
[ApiController]
[Route("api/dynamo-set")]
public class DynamoSetController : ControllerBase
{
    private readonly ShowcaseContext _context;

    public DynamoSetController(ShowcaseContext context) => _context = context;

    private IDynamoDbSet<Order> Orders => _context.Orders;

    /// <summary>set.ToListAsync - full table materialization.</summary>
    [HttpGet("list")]
    public async Task<ActionResult> List(CancellationToken ct)
        => Ok((await Orders.ToListAsync(ct))?.Select(o => new { o.Id, o.CustomerEmail, o.Quantity }));

    /// <summary>set.CountAsync() and CountAsync(predicate).</summary>
    [HttpGet("count")]
    public async Task<ActionResult> Count([FromQuery] int minQuantity = 0, CancellationToken ct = default)
        => Ok(new
        {
            Total = await Orders.CountAsync(ct),
            Matching = await Orders.CountAsync(o => o.Quantity >= minQuantity, ct)
        });

    /// <summary>set.ToPagedListAsync(predicate, page, pageSize).</summary>
    [HttpGet("paged")]
    public async Task<ActionResult> Paged([FromQuery] int minQuantity = 0, [FromQuery] int page = 1, [FromQuery] int pageSize = 5, CancellationToken ct = default)
    {
        var result = await Orders.ToPagedListAsync(o => o.Quantity >= minQuantity, page, pageSize, ct);
        return Ok(new { result.Page, result.PageSize, result.TotalRecords, Items = result.Items.Select(o => o.Id) });
    }

    /// <summary>set.FirstOrDefaultAsync / FirstAsync / SingleOrDefaultAsync / SingleAsync - all four terminal operators.</summary>
    [HttpGet("terminal-operators/{id}")]
    public async Task<ActionResult> TerminalOperators(string id, CancellationToken ct)
    {
        // The id predicate is unique, so Single* are safe here.
        var firstOrDefault = await Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
        var first = await Orders.FirstAsync(o => o.Id == id, ct);
        var singleOrDefault = await Orders.SingleOrDefaultAsync(o => o.Id == id, ct);
        var single = await Orders.SingleAsync(o => o.Id == id, ct);
        var anyItem = await Orders.FirstOrDefaultAsync(ct); // parameterless variant

        return Ok(new
        {
            FirstOrDefault = firstOrDefault?.Id,
            First = first?.Id,
            SingleOrDefault = singleOrDefault?.Id,
            Single = single?.Id,
            AnyItemInTable = anyItem?.Id
        });
    }

    /// <summary>set.FindAsync(hash, range) - both overloads.</summary>
    [HttpGet("find")]
    public async Task<ActionResult> Find([FromQuery] string partitionKey, [FromQuery] string id, CancellationToken ct)
    {
        var byParams = await Orders.FindAsync(partitionKey, id);
        var byArray = await Orders.FindAsync(new object[] { partitionKey, id }, ct);
        return Ok(new { ByParams = byParams?.Id, ByArrayOverload = byArray?.Id });
    }

    /// <summary>set.GetItemsAsync - filter/sort/limit, partition-key, and partition+predicate overloads.</summary>
    [HttpGet("items")]
    public async Task<ActionResult> GetItems([FromQuery] string partitionKey = "order", [FromQuery] int minQuantity = 0, [FromQuery] int limit = 10, CancellationToken ct = default)
        => Ok(new
        {
            FilteredLimited = (await Orders.GetItemsAsync(o => o.Quantity >= minQuantity, sortDescending: true, limit, ct)).Select(o => o.Id),
            ByPartition = (await Orders.GetItemsAsync(partitionKey, ct)).Select(o => o.Id),
            ByPartitionAndPredicate = (await Orders.GetItemsAsync(partitionKey, o => o.Quantity >= minQuantity, ct)).Select(o => o.Id)
        });

    /// <summary>
    /// set.ExecuteQueryAsync with the full QueryExpression fluent builder:
    /// WithPartitionKey / WithIndex / AddFilterExpression (lambda + raw string) /
    /// OrderByDescending / PagingLimit / WithProjectionAttributes / AddExpressionAttributeNames.
    /// </summary>
    [HttpGet("query-expression")]
    public async Task<ActionResult> QueryExpression([FromQuery] string? customerEmail, [FromQuery] int minQuantity = 0, [FromQuery] int limit = 25, CancellationToken ct = default)
    {
        var query = new QueryExpression<Order>()
            .AddFilterExpression(o => o.Quantity >= minQuantity)
            .AddFilterExpression("Price >= :minPrice", new Dictionary<string, object> { [":minPrice"] = 0d })
            .OrderByDescending()
            .PagingLimit(limit);

        if (!string.IsNullOrEmpty(customerEmail))
        {
            // Targets the CustomerEmail GSI declared via HasIndex in OrderConfiguration.
            query.WithIndex("CustomerEmail-index")
                 .WithPartitionKey("CustomerEmail", customerEmail);
        }

        var results = await Orders.ExecuteQueryAsync(query, ct);
        return Ok(results?.Select(o => new { o.Id, o.CustomerEmail, o.Quantity, o.Price }));
    }

    /// <summary>set.AddAsync + AddRangeAsync (both overloads) - immediate writes, no Unit of Work.</summary>
    [HttpPost("add")]
    public async Task<ActionResult> Add([FromQuery] string email = "direct@example.com", CancellationToken ct = default)
    {
        var single = NewOrder(email, "Direct-Single");
        await Orders.AddAsync(single, ct);

        var rangeA = NewOrder(email, "Direct-Range-A");
        var rangeB = NewOrder(email, "Direct-Range-B");
        await Orders.AddRangeAsync(new List<Order> { rangeA }, ct);   // IEnumerable overload
        await Orders.AddRangeAsync(rangeB);                            // params overload

        return Ok(new { Created = new[] { single.Id, rangeA.Id, rangeB.Id } });
    }

    /// <summary>set.UpdateAsync + UpdateRangeAsync - immediate updates with ETag rotation.</summary>
    [HttpPut("update/{id}")]
    public async Task<ActionResult> Update(string id, [FromQuery] int quantity = 99, CancellationToken ct = default)
    {
        var order = await Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }

        order.Quantity = quantity;
        var updated = await Orders.UpdateAsync(order, ct);
        await Orders.UpdateRangeAsync(new List<Order> { updated }, ct);
        return Ok(new { updated.Id, updated.Quantity });
    }

    /// <summary>set.RemoveAsync + RemoveRangeAsync - immediate deletes.</summary>
    [HttpDelete("remove")]
    public async Task<ActionResult> Remove([FromQuery] string email = "direct@example.com", CancellationToken ct = default)
    {
        var orders = await Orders.GetItemsAsync(o => o.CustomerEmail == email, false, null, ct);
        if (orders.Count == 0)
        {
            return Ok(new { Removed = 0 });
        }

        await Orders.RemoveAsync(orders[0], ct);
        if (orders.Count > 1)
        {
            await Orders.RemoveRangeAsync(orders.Skip(1), ct);
        }
        return Ok(new { Removed = orders.Count });
    }

    /// <summary>
    /// Context-level change tracking: AddOriginalEntity/AddOriginalEntities attach
    /// loaded entities, a mutation is auto-detected (no explicit Update call) and
    /// SaveChangesAsync persists it atomically; context.Add/Update/Delete round out
    /// the BaseDynamoDbContext surface.
    /// </summary>
    [HttpPost("context-tracking-demo")]
    public async Task<ActionResult> ContextTrackingDemo(CancellationToken ct)
    {
        // context.Add + SaveChangesAsync
        var created = NewOrder("tracking@example.com", "Tracked");
        _context.Add(created);
        await _context.SaveChangesAsync(ct);

        // Attach as "original", mutate WITHOUT calling Update - change detection finds it.
        var loaded = await Orders.FirstOrDefaultAsync(o => o.Id == created.Id, ct);
        var tracked = _context.AddOriginalEntity(loaded!);
        var trackedMany = _context.AddOriginalEntities(new[] { tracked }).ToList();
        tracked.Tags.Add("auto-detected");
        var written = await _context.SaveChangesAsync(ct);

        // context.Update + context.Delete + SaveEntitiesAsync (dispatches domain events too)
        _context.Update(tracked);
        _context.Delete(tracked);
        await _context.SaveEntitiesAsync(ct);

        return Ok(new
        {
            CreatedId = created.Id,
            AttachedCount = trackedMany.Count,
            AutoDetectedWrites = written,
            FinalState = "deleted"
        });
    }

    private static Order NewOrder(string email, string product)
    {
        var order = new Order { CustomerEmail = email, ProductName = product, Quantity = 1, Price = 9.99 };
        order.SetId(DynamoUtils.GenerateId(order));
        return order;
    }
}
