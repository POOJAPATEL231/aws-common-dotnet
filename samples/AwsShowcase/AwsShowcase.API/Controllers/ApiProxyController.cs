using AwsShowcase.Core.Orders;
using Infrastructure.Common.AWS.ApiService;
using Microsoft.AspNetCore.Mvc;

namespace AwsShowcase.API.Controllers;

/// <summary>
/// Service-to-service HTTP via IApiService - every method, demonstrated by calling
/// THIS API's own /api/orders endpoints through the named "Showcase" HttpClient
/// (auth headers, API keys and client IPs are forwarded automatically).
/// </summary>
[ApiController]
[Route("api/api-service")]
public class ApiProxyController : ControllerBase
{
    private const string Service = "Showcase";
    private readonly IApiService _api;
    private readonly ICognitoHttpClient _cognito;

    public ApiProxyController(IApiService api, ICognitoHttpClient cognito)
    {
        _api = api;
        _cognito = cognito;
    }

    /// <summary>GetAsync&lt;T&gt;(service, url) - plain GET.</summary>
    [HttpGet("orders")]
    public async Task<ActionResult> GetOrders(CancellationToken ct)
        => Ok(await _api.GetAsync<object>(Service, "api/orders/all", ct));

    /// <summary>GetAsync&lt;T&gt;(service, url, queryParameters) - GET with query-string building.</summary>
    [HttpGet("orders/search")]
    public async Task<ActionResult> Search([FromQuery] int minQuantity = 1, CancellationToken ct = default)
        => Ok(await _api.GetAsync<object>(Service, "api/orders/search",
            new Dictionary<string, object?> { ["minQuantity"] = minQuantity, ["mostExpensiveFirst"] = true }, ct));

    /// <summary>PostAsync&lt;TReq,TRes&gt; - POST returning a body (creates a real order via the CQRS endpoint).</summary>
    [HttpPost("orders")]
    public async Task<ActionResult> Create([FromQuery] string email = "proxy@example.com", CancellationToken ct = default)
        => Ok(await _api.PostAsync<CreateOrderCommand, object>(Service, "api/orders",
            new CreateOrderCommand(email, "Via-ApiService", 1, 42.0), ct));

    /// <summary>PostAsync&lt;TReq&gt; - fire-and-forget POST (no response body expected).</summary>
    [HttpPost("orders/fire-and-forget")]
    public async Task<IActionResult> CreateFireAndForget([FromQuery] string email = "proxy@example.com", CancellationToken ct = default)
    {
        await _api.PostAsync(Service, "api/orders",
            new CreateOrderCommand(email, "Via-ApiService-NoResponse", 1, 10.0), ct);
        return Accepted();
    }

    /// <summary>PostContentAsync&lt;T&gt; - multipart/form-data upload through the S3 endpoint.</summary>
    [HttpPost("upload")]
    public async Task<ActionResult> Upload([FromQuery] string container = "showcase-files", CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent("hello from IApiService"u8.ToArray()), "file", "proxy.txt" }
        };
        return Ok(await _api.PostContentAsync<object>(Service, $"api/files/{container}?prefix=proxy", content, ct));
    }

    /// <summary>PutAsync&lt;TReq,TRes&gt; + PutAsync&lt;TReq&gt; - status update through the CQRS endpoint.</summary>
    [HttpPut("orders/{id}/status")]
    public async Task<ActionResult> UpdateStatus(string id, [FromQuery] bool expectResponse = true, CancellationToken ct = default)
    {
        if (expectResponse)
        {
            return Ok(await _api.PutAsync<object, object>(Service, $"api/orders/{id}/status?status=Paid", new { }, ct));
        }

        await _api.PutAsync(Service, $"api/orders/{id}/status?status=Paid", new { }, ct);
        return NoContent();
    }

    /// <summary>DeleteAsync + DeleteAsync&lt;T&gt; - both delete variants.</summary>
    [HttpDelete("orders/{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] bool expectResponse = false, CancellationToken ct = default)
    {
        if (expectResponse)
        {
            return Ok(await _api.DeleteAsync<object>(Service, $"api/orders/{id}", ct));
        }

        await _api.DeleteAsync(Service, $"api/orders/{id}", ct);
        return NoContent();
    }

    /// <summary>ICognitoHttpClient.GetTokenAsync - client-credentials token (needs a real/emulated Cognito domain).</summary>
    [HttpPost("cognito-token")]
    public async Task<ActionResult> GetToken([FromQuery] string clientId, [FromQuery] string clientSecret, [FromQuery] string scope, CancellationToken ct)
    {
        var token = await _cognito.GetTokenAsync(clientId, clientSecret, scope, ct);
        return Ok(new { HasToken = !string.IsNullOrEmpty(token?.Token), token?.ErrorMessage });
    }
}
