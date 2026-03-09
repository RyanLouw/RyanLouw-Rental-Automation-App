using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RLRentalApp.Web.Api.DTOs;
using RLRentalApp.Web.Api.Services;

namespace RLRentalApp.Web.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/automation")]
public sealed class AutomationController : ControllerBase
{
    private readonly IAutomationService _automationService;
    private readonly ILogger<AutomationController> _logger;

    public AutomationController(IAutomationService automationService, ILogger<AutomationController> logger)
    {
        _automationService = automationService;
        _logger = logger;
    }

    [HttpGet("properties/{propertyId:int}/status")]
    [ProducesResponseType(typeof(PropertyStatusResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPropertyStatus([FromRoute] int propertyId)
    {
        var status = await _automationService.GetPropertyStatusAsync(propertyId);
        if (status is null)
        {
            return NotFound();
        }

        return Ok(status);
    }

    [HttpPost("rent")]
    [ProducesResponseType(typeof(AutomationCommandResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveRent([FromBody] SaveRentRequestDto request)
    {
        return await ExecuteCommandAsync(() => _automationService.SaveRentAsync(request));
    }

    [HttpPost("services")]
    [ProducesResponseType(typeof(AutomationCommandResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveServices([FromBody] SaveServicesRequestDto request)
    {
        return await ExecuteCommandAsync(() => _automationService.SaveServicesAsync(request));
    }

    [HttpPost("payments")]
    [ProducesResponseType(typeof(AutomationCommandResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SavePayments([FromBody] SavePaymentsRequestDto request)
    {
        return await ExecuteCommandAsync(() => _automationService.SavePaymentsAsync(request));
    }

    private async Task<IActionResult> ExecuteCommandAsync(Func<Task<AutomationCommandResponseDto>> command)
    {
        try
        {
            var result = await command();
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected automation API error.");
            return Problem(
                detail: "An unexpected error occurred while processing the automation request.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
