using Application.Common.Event;
using AwsShowcase.Entity;
using AwsShowcase.Integration.Handlers;
using Infrastructure.Common.AWS.Eventbus;
using Microsoft.AspNetCore.Mvc;
using Persistence.Common.AWS;
using Utils.Common.Crypto;

namespace AwsShowcase.API.Controllers;

/// <summary>Secrets Manager + KMS - every ISecretRepository method.</summary>
[ApiController]
[Route("api/secrets")]
public class SecretsController : ControllerBase
{
    private readonly ISecretRepository _secrets;

    public SecretsController(ISecretRepository secrets) => _secrets = secrets;

    /// <summary>UpsertSecretAsync - create or update a secret.</summary>
    [HttpPut("{name}")]
    public async Task<ActionResult<string>> Upsert(string name, [FromBody] string value, CancellationToken ct)
        => Ok(await _secrets.UpsertSecretAsync(name, value, ct));

    /// <summary>GetSecretValueAsync.</summary>
    [HttpGet("{name}")]
    public async Task<ActionResult<string>> Get(string name, CancellationToken ct)
        => Ok(await _secrets.GetSecretValueAsync(name, ct));

    /// <summary>DeleteSecretAsync.</summary>
    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(string name, CancellationToken ct)
    {
        await _secrets.DeleteSecretAsync(name, ct);
        return NoContent();
    }

    /// <summary>WrapKeyAsync + UnWrapKeyAsync - KMS envelope-encryption round trip.</summary>
    [HttpPost("wrap-key-roundtrip")]
    public async Task<ActionResult> WrapRoundTrip([FromQuery] string kmsKeyName, CancellationToken ct)
    {
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var wrapped = await _secrets.WrapKeyAsync(key, kmsKeyName, ct);
        var unwrapped = await _secrets.UnWrapKeyAsync(wrapped, kmsKeyName, ct);
        return Ok(new { Wrapped = wrapped, RoundTripOk = key.SequenceEqual(unwrapped) });
    }
}

/// <summary>DynamoDB table administration - every IDynamoDbTableProvider method.</summary>
[ApiController]
[Route("api/tables")]
public class TablesController : ControllerBase
{
    private readonly IDynamoDbTableProvider _tables;

    // Multiple providers are registered (one per entity); the Orders one is representative.
    public TablesController(IEnumerable<IDynamoDbTableProvider> tableProviders)
        => _tables = tableProviders.First(p => p.GetType().GenericTypeArguments.FirstOrDefault() == typeof(Order));

    /// <summary>ListTablesAsync - descriptions of every table in the account/region.</summary>
    [HttpGet]
    public async Task<ActionResult> List(CancellationToken ct)
        => Ok((await _tables.ListTablesAsync(ct)).Select(t => new { t.TableName, Status = t.TableStatus?.Value, t.ItemCount }));

    /// <summary>TableExistsAsync + CreateTableAsync + EnableTtlAsync - full provisioning cycle.</summary>
    [HttpPost("ensure")]
    public async Task<ActionResult> Ensure(CancellationToken ct)
    {
        var existed = await _tables.TableExistsAsync(ct);
        var created = !existed && await _tables.CreateTableAsync(5, 5, ct);
        var ttlEnabled = await _tables.EnableTtlAsync(ct);
        return Ok(new { AlreadyExisted = existed, Created = created, TtlEnabled = ttlEnabled });
    }

    /// <summary>UpdateTableAsync - change provisioned throughput.</summary>
    [HttpPut("capacity")]
    public async Task<ActionResult<bool>> UpdateCapacity([FromQuery] long read = 10, [FromQuery] long write = 10, CancellationToken ct = default)
        => Ok(await _tables.UpdateTableAsync(read, write, ct));

    /// <summary>DeleteTableAsync - removes the orders table (demo only!).</summary>
    [HttpDelete]
    public async Task<ActionResult<bool>> Delete(CancellationToken ct)
        => Ok(await _tables.DeleteTableAsync(ct));
}

/// <summary>SNS/SQS event bus - every IEventBus method. Routing: OrderCreatedIntegrationEvent
/// -> topic "ordercreated.fifo" -> queue "ordercreated_Queue.fifo" (+DLQ), tuned via AwsInfraSettings:Queues.</summary>
[ApiController]
[Route("api/sns-bus")]
public class SnsEventBusController : ControllerBase
{
    private readonly IEventBus _bus;

    public SnsEventBusController(IEventBus bus) => _bus = bus;

    /// <summary>PublishAsync - serializes the event and publishes to its convention-named topic.</summary>
    [HttpPost("publish")]
    public async Task<IActionResult> Publish([FromQuery] string orderId, [FromQuery] string email, [FromQuery] string product)
    {
        await _bus.PublishAsync(new OrderCreatedIntegrationEvent(orderId, email, product));
        return Accepted();
    }

    /// <summary>SubscribeAsync&lt;T,TH&gt; - creates topic+queue+DLQ and registers the typed handler.</summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe()
    {
        await _bus.SubscribeAsync<OrderCreatedIntegrationEvent, OrderCreatedEventHandler>();
        return Ok(new { Subscribed = nameof(OrderCreatedIntegrationEvent), Handler = nameof(OrderCreatedEventHandler) });
    }

    /// <summary>UnsubscribeAsync&lt;T,TH&gt;.</summary>
    [HttpDelete("subscribe")]
    public async Task<IActionResult> Unsubscribe()
    {
        await _bus.UnsubscribeAsync<OrderCreatedIntegrationEvent, OrderCreatedEventHandler>();
        return NoContent();
    }

    /// <summary>SubscribeDynamicAsync - both the generic and the Type overload.</summary>
    [HttpPost("subscribe-dynamic/{eventName}")]
    public async Task<IActionResult> SubscribeDynamic(string eventName, [FromQuery] bool useTypeOverload = false)
    {
        if (useTypeOverload)
        {
            await _bus.SubscribeDynamicAsync(eventName, typeof(DynamicLoggingEventHandler));
        }
        else
        {
            await _bus.SubscribeDynamicAsync<DynamicLoggingEventHandler>(eventName);
        }
        return Ok(new { Subscribed = eventName });
    }

    /// <summary>UnsubscribeDynamicAsync - both overloads.</summary>
    [HttpDelete("subscribe-dynamic/{eventName}")]
    public async Task<IActionResult> UnsubscribeDynamic(string eventName, [FromQuery] bool useTypeOverload = false)
    {
        if (useTypeOverload)
        {
            await _bus.UnsubscribeDynamicAsync(eventName, typeof(DynamicLoggingEventHandler));
        }
        else
        {
            await _bus.UnsubscribeDynamicAsync<DynamicLoggingEventHandler>(eventName);
        }
        return NoContent();
    }
}

/// <summary>SQS queue service - every IQueueService method.</summary>
[ApiController]
[Route("api/queue")]
public class QueueController : ControllerBase
{
    private readonly Application.Common.Event.IQueueService _queue;

    public QueueController(Application.Common.Event.IQueueService queue) => _queue = queue;

    /// <summary>SendMessageAsync - single message; returns the SQS message id.</summary>
    [HttpPost("send")]
    public async Task<ActionResult<string>> Send([FromBody] object message, CancellationToken ct)
        => Ok(await _queue.SendMessageAsync(message, ct));

    /// <summary>SendMessagesAsync - ordered batch; returns the session id.</summary>
    [HttpPost("send-batch")]
    public async Task<ActionResult<string>> SendBatch([FromBody] List<object> messages, CancellationToken ct)
        => Ok(await _queue.SendMessagesAsync(messages, ct));

    /// <summary>ScheduleMessageAsync - delayed delivery.</summary>
    [HttpPost("schedule")]
    public async Task<ActionResult<string>> Schedule([FromBody] object message, [FromQuery] int delaySeconds = 30, CancellationToken ct = default)
        => Ok(await _queue.ScheduleMessageAsync(message, TimeSpan.FromSeconds(delaySeconds), ct));
}

/// <summary>
/// In-memory subscription manager - a transcript endpoint that exercises EVERY
/// IEventBusSubscriptionsManager method in one lifecycle so the routing bookkeeping
/// is observable without infrastructure.
/// </summary>
[ApiController]
[Route("api/subscription-manager")]
public class SubscriptionManagerController : ControllerBase
{
    private readonly IEventBusSubscriptionsManager _manager;

    public SubscriptionManagerController(IEventBusSubscriptionsManager manager) => _manager = manager;

    [HttpPost("lifecycle-demo")]
    public ActionResult LifecycleDemo()
    {
        var transcript = new List<string>();
        string? removedEvent = null;
        _manager.OnEventRemoved += (_, name) => removedEvent = name;

        transcript.Add($"IsEmpty (start): {_manager.IsEmpty}");

        _manager.AddSubscription<OrderCreatedIntegrationEvent, OrderCreatedEventHandler>();
        transcript.Add("AddSubscription<OrderCreated, OrderCreatedEventHandler>");

        _manager.AddDynamicSubscription<DynamicLoggingEventHandler>("price-changed");
        _manager.AddDynamicSubscription("stock-changed", typeof(DynamicLoggingEventHandler));
        transcript.Add("AddDynamicSubscription (generic + Type overloads)");

        transcript.Add($"HasSubscriptionsForEvent<T>: {_manager.HasSubscriptionsForEvent<OrderCreatedIntegrationEvent>()}");
        transcript.Add($"HasSubscriptionsForEvent(name): {_manager.HasSubscriptionsForEvent("price-changed")}");
        transcript.Add($"GetEventKey<T>: {_manager.GetEventKey<OrderCreatedIntegrationEvent>()}");
        transcript.Add($"GetEventTypeByName: {_manager.GetEventTypeByName(nameof(OrderCreatedIntegrationEvent))?.Name}");
        transcript.Add($"GetHandlersForEvent<T>: {_manager.GetHandlersForEvent<OrderCreatedIntegrationEvent>().Count()} handler(s)");
        transcript.Add($"GetHandlersForEvent(name): {_manager.GetHandlersForEvent("stock-changed").Count()} handler(s)");

        _manager.RemoveDynamicSubscription<DynamicLoggingEventHandler>("price-changed");
        _manager.RemoveDynamicSubscription("stock-changed", typeof(DynamicLoggingEventHandler));
        _manager.RemoveSubscription<OrderCreatedIntegrationEvent, OrderCreatedEventHandler>();
        transcript.Add($"Removed all; OnEventRemoved fired for: {removedEvent}");

        _manager.Clear();
        transcript.Add($"IsEmpty (after Clear): {_manager.IsEmpty}");

        return Ok(transcript);
    }
}
