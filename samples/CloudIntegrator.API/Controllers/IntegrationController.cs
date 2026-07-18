using CloudIntegrator.Core.Interfaces;
using CloudIntegrator.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace CloudIntegrator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IntegrationController : ControllerBase
{
    private readonly IIntegrationService _integrationService;
    private readonly ILogger<IntegrationController> _logger;

    public IntegrationController(
        IIntegrationService integrationService,
        ILogger<IntegrationController> logger)
    {
        _integrationService = integrationService;
        _logger = logger;
    }

    [HttpPost("integrate")]
    public async Task<ActionResult<IntegrationResult>> Integrate([FromBody] IntegrationRequest request)
    {
        try
        {
            var result = await _integrationService.IntegrateAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Integration failed");
            return StatusCode(500, new { message = "Integration failed", error = ex.Message });
        }
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<IntegrationResult>>> GetHistory([FromQuery] int limit = 100)
    {
        var history = await _integrationService.GetHistoryAsync(limit);
        return Ok(history);
    }

    [HttpGet("status")]
    public ActionResult GetStatus()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
