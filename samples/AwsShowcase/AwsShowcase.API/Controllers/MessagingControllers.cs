using Application.Common.Email;
using Application.Common.Event;
using Application.Common.Scheduling;
using AwsShowcase.Entity;
using Infrastructure.Common.AWS.Streaming;
using Microsoft.AspNetCore.Mvc;

namespace AwsShowcase.API.Controllers;

/// <summary>SES email - every IEmailService method.</summary>
[ApiController]
[Route("api/email")]
public class EmailController : ControllerBase
{
    private readonly IEmailService _email;

    public EmailController(IEmailService email)
    {
        _email = email;
    }

    /// <summary>SendAsync - full message (to/cc/bcc, text + HTML bodies, reply-to).</summary>
    [HttpPost("send")]
    public async Task<ActionResult<string>> Send([FromBody] EmailMessage message, CancellationToken ct)
        => Ok(await _email.SendAsync(message, ct));

    /// <summary>SendTemplatedAsync - SES template + substitution data.</summary>
    [HttpPost("send-templated")]
    public async Task<ActionResult<string>> SendTemplated([FromQuery] string from, [FromQuery] string to,
        [FromQuery] string template, [FromBody] Dictionary<string, string> data, CancellationToken ct)
        => Ok(await _email.SendTemplatedAsync(from, new[] { to }, template, data, ct));
}

/// <summary>EventBridge - IIntegrationEventPublisher.PublishAsync.</summary>
[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly IIntegrationEventPublisher _publisher;

    public EventsController(IIntegrationEventPublisher publisher)
    {
        _publisher = publisher;
    }

    /// <summary>Publishes an OrderCreated event directly to EventBridge (the outbox uses the same publisher).</summary>
    [HttpPost("order-created")]
    public async Task<IActionResult> PublishOrderCreated([FromQuery] string orderId, [FromQuery] string email, [FromQuery] string product, CancellationToken ct)
    {
        await _publisher.PublishAsync(new OrderCreatedIntegrationEvent(orderId, email, product), ct);
        return Accepted();
    }
}

/// <summary>EventBridge Scheduler - every IScheduler method.</summary>
[ApiController]
[Route("api/schedules")]
public class SchedulesController : ControllerBase
{
    private readonly IScheduler _scheduler;

    public SchedulesController(IScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <summary>CreateOrUpdateScheduleAsync - e.g. expression "rate(5 minutes)" or "cron(0 2 * * ? *)".</summary>
    [HttpPut("{name}")]
    public async Task<IActionResult> CreateOrUpdate(string name, [FromQuery] string expression,
        [FromQuery] string targetArn, [FromQuery] string roleArn, [FromBody] object? payload, CancellationToken ct)
    {
        await _scheduler.CreateOrUpdateScheduleAsync(name, expression, targetArn, roleArn,
            payload is null ? null : System.Text.Json.JsonSerializer.Serialize(payload), ct);
        return NoContent();
    }

    /// <summary>DeleteScheduleAsync - no-op when missing.</summary>
    [HttpDelete("{name}")]
    public async Task<IActionResult> Delete(string name, CancellationToken ct)
    {
        await _scheduler.DeleteScheduleAsync(name, ct);
        return NoContent();
    }
}

/// <summary>Kinesis + Firehose - every IStreamPublisher method on both implementations.</summary>
[ApiController]
[Route("api/streams")]
public class StreamsController : ControllerBase
{
    private readonly KinesisStreamPublisher _kinesis;
    private readonly FirehoseStreamPublisher _firehose;

    public StreamsController(KinesisStreamPublisher kinesis, FirehoseStreamPublisher firehose)
    {
        _kinesis = kinesis;
        _firehose = firehose;
    }

    /// <summary>Kinesis PublishAsync - single record. Defaults to the demo stream
    /// provisioned at startup ("showcase-stream").</summary>
    [HttpPost("kinesis/{stream}")]
    public async Task<IActionResult> PublishKinesis(string stream, [FromBody] object record, [FromQuery] string? partitionKey, CancellationToken ct)
    {
        await _kinesis.PublishAsync(stream, record, partitionKey, ct);
        return Accepted();
    }

    /// <summary>Kinesis PublishBatchAsync - returns accepted record count.</summary>
    [HttpPost("kinesis/{stream}/batch")]
    public async Task<ActionResult<int>> PublishKinesisBatch(string stream, [FromBody] List<object> records, CancellationToken ct)
        => Ok(await _kinesis.PublishBatchAsync(stream, records, ct));

    /// <summary>Firehose PublishAsync - single record to a delivery stream.</summary>
    [HttpPost("firehose/{stream}")]
    public async Task<IActionResult> PublishFirehose(string stream, [FromBody] object record, CancellationToken ct)
    {
        await _firehose.PublishAsync(stream, record, partitionKey: null, ct);
        return Accepted();
    }

    /// <summary>Firehose PublishBatchAsync - returns accepted record count.</summary>
    [HttpPost("firehose/{stream}/batch")]
    public async Task<ActionResult<int>> PublishFirehoseBatch(string stream, [FromBody] List<object> records, CancellationToken ct)
        => Ok(await _firehose.PublishBatchAsync(stream, records, ct));
}
