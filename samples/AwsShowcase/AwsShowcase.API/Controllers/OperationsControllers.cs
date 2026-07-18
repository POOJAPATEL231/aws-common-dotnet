using Application.Common.Identity;
using Application.Common.Locking;
using Application.Common.Metrics;
using Application.Common.Workflow;
using Infrastructure.Common.AWS.FeatureManager;
using Microsoft.AspNetCore.Mvc;

namespace AwsShowcase.API.Controllers;

/// <summary>Cognito user administration - every IIdentityService method.</summary>
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IIdentityService _identity;

    public UsersController(IIdentityService identity)
    {
        _identity = identity;
    }

    /// <summary>CreateUserAsync - optional attributes, invite suppression.</summary>
    [HttpPost]
    public async Task<ActionResult<IdentityUser>> Create([FromQuery] string userName, [FromQuery] string? email,
        [FromBody] Dictionary<string, string>? attributes, [FromQuery] bool suppressInvite = false, CancellationToken ct = default)
        => Ok(await _identity.CreateUserAsync(userName, email, attributes, suppressInvite, ct));

    /// <summary>GetUserAsync - null-safe lookup.</summary>
    [HttpGet("{userName}")]
    public async Task<ActionResult<IdentityUser>> Get(string userName, CancellationToken ct)
    {
        var user = await _identity.GetUserAsync(userName, ct);
        return user is null ? NotFound() : Ok(user);
    }

    /// <summary>DeleteUserAsync.</summary>
    [HttpDelete("{userName}")]
    public async Task<IActionResult> Delete(string userName, CancellationToken ct)
    {
        await _identity.DeleteUserAsync(userName, ct);
        return NoContent();
    }

    /// <summary>SetUserEnabledAsync - enable/disable sign-in.</summary>
    [HttpPut("{userName}/enabled")]
    public async Task<IActionResult> SetEnabled(string userName, [FromQuery] bool enabled, CancellationToken ct)
    {
        await _identity.SetUserEnabledAsync(userName, enabled, ct);
        return NoContent();
    }

    /// <summary>SetPasswordAsync - permanent or force-change.</summary>
    [HttpPut("{userName}/password")]
    public async Task<IActionResult> SetPassword(string userName, [FromBody] string password, [FromQuery] bool permanent = true, CancellationToken ct = default)
    {
        await _identity.SetPasswordAsync(userName, password, permanent, ct);
        return NoContent();
    }

    /// <summary>AddToGroupAsync.</summary>
    [HttpPut("{userName}/groups/{groupName}")]
    public async Task<IActionResult> AddToGroup(string userName, string groupName, CancellationToken ct)
    {
        await _identity.AddToGroupAsync(userName, groupName, ct);
        return NoContent();
    }

    /// <summary>RemoveFromGroupAsync.</summary>
    [HttpDelete("{userName}/groups/{groupName}")]
    public async Task<IActionResult> RemoveFromGroup(string userName, string groupName, CancellationToken ct)
    {
        await _identity.RemoveFromGroupAsync(userName, groupName, ct);
        return NoContent();
    }
}

/// <summary>Step Functions - every IWorkflowClient method.</summary>
[ApiController]
[Route("api/workflows")]
public class WorkflowsController : ControllerBase
{
    private readonly IWorkflowClient _workflows;

    public WorkflowsController(IWorkflowClient workflows)
    {
        _workflows = workflows;
    }

    /// <summary>StartAsync - name or full ARN; JSON input.</summary>
    [HttpPost("{workflowName}/executions")]
    public async Task<ActionResult<string>> Start(string workflowName, [FromBody] object input, [FromQuery] string? executionName, CancellationToken ct)
        => Ok(await _workflows.StartAsync(workflowName, input, executionName, ct));

    /// <summary>GetExecutionAsync - status/output of a running or finished execution.</summary>
    [HttpGet("executions")]
    public async Task<ActionResult<WorkflowExecution>> GetExecution([FromQuery] string executionId, CancellationToken ct)
        => Ok(await _workflows.GetExecutionAsync(executionId, ct));

    /// <summary>StopAsync - aborts a running execution.</summary>
    [HttpDelete("executions")]
    public async Task<IActionResult> Stop([FromQuery] string executionId, [FromQuery] string? reason, CancellationToken ct)
    {
        await _workflows.StopAsync(executionId, reason, ct);
        return NoContent();
    }

    /// <summary>SendTaskSuccessAsync - completes a callback-pattern task.</summary>
    [HttpPost("tasks/success")]
    public async Task<IActionResult> TaskSuccess([FromQuery] string taskToken, [FromBody] object output, CancellationToken ct)
    {
        await _workflows.SendTaskSuccessAsync(taskToken, output, ct);
        return NoContent();
    }

    /// <summary>SendTaskFailureAsync - fails a callback-pattern task.</summary>
    [HttpPost("tasks/failure")]
    public async Task<IActionResult> TaskFailure([FromQuery] string taskToken, [FromQuery] string error, [FromQuery] string? cause, CancellationToken ct)
    {
        await _workflows.SendTaskFailureAsync(taskToken, error, cause, ct);
        return NoContent();
    }
}

/// <summary>DynamoDB distributed lock - IDistributedLock.TryAcquireAsync + release.</summary>
[ApiController]
[Route("api/locks")]
public class LocksController : ControllerBase
{
    private readonly IDistributedLock _lock;

    public LocksController(IDistributedLock distributedLock)
    {
        _lock = distributedLock;
    }

    /// <summary>Acquires the lock, simulates work, then releases (DisposeAsync). Second concurrent call returns 409.</summary>
    [HttpPost("{name}/run")]
    public async Task<IActionResult> RunExclusive(string name, [FromQuery] int leaseSeconds = 30, [FromQuery] int workMs = 500, CancellationToken ct = default)
    {
        await using var handle = await _lock.TryAcquireAsync(name, TimeSpan.FromSeconds(leaseSeconds), ct);
        if (handle is null)
        {
            return Conflict(new { message = $"Lock '{name}' is held by another owner." });
        }

        await Task.Delay(workMs, ct); // the "exclusive work"
        return Ok(new { handle.LockName, handle.OwnerId, handle.LeaseExpiresAtUtc });
    }
}

/// <summary>Feature flags - every IFeatureManager method.</summary>
[ApiController]
[Route("api/features")]
public class FeaturesController : ControllerBase
{
    private readonly IFeatureManager _features;

    public FeaturesController(IFeatureManager features)
    {
        _features = features;
    }

    /// <summary>GetFeatureNamesAsync - all known flags.</summary>
    [HttpGet]
    public async Task<ActionResult<List<string>>> List()
    {
        var names = new List<string>();
        await foreach (var name in _features.GetFeatureNamesAsync())
        {
            names.Add(name);
        }
        return Ok(names);
    }

    /// <summary>IsEnabledAsync - both the plain and the context overload.</summary>
    [HttpGet("{feature}")]
    public async Task<ActionResult> IsEnabled(string feature)
        => Ok(new
        {
            Enabled = await _features.IsEnabledAsync(feature),
            EnabledForUser = await _features.IsEnabledAsync(feature, new { UserId = 1 })
        });
}

/// <summary>CloudWatch EMF metrics - every IMetrics method.</summary>
[ApiController]
[Route("api/metrics")]
public class MetricsController : ControllerBase
{
    private readonly IMetrics _metrics;

    public MetricsController(IMetrics metrics)
    {
        _metrics = metrics;
    }

    /// <summary>Count + Gauge + Duration + TimeAsync in one demo call - watch the EMF JSON lines in the console.</summary>
    [HttpPost("demo")]
    public async Task<ActionResult> EmitDemo([FromQuery] string name = "ShowcaseDemo")
    {
        _metrics.Count($"{name}.Requests");
        _metrics.Gauge($"{name}.QueueDepth", 42, new Dictionary<string, string> { ["Queue"] = "orders" });
        _metrics.Duration($"{name}.ExternalCall", TimeSpan.FromMilliseconds(123));

        var timed = await _metrics.TimeAsync($"{name}.TimedWork", async () =>
        {
            await Task.Delay(25);
            return "done";
        });

        return Ok(new { timed, note = "Four EMF JSON lines were written to stdout - CloudWatch turns them into metrics automatically." });
    }
}
