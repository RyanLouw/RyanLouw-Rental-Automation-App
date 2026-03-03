using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RLRentalApp.Models;
using RLRentalApp.Web.Managers;
using System.Diagnostics;

namespace RLRentalApp.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IPropertyDashboardManager _propertyDashboardManager;

    public HomeController(ILogger<HomeController> logger, IPropertyDashboardManager propertyDashboardManager)
    {
        _logger = logger;
        _propertyDashboardManager = propertyDashboardManager;
    }

    public async Task<IActionResult> Index()
    {
        var vm = await _propertyDashboardManager.GetDashboardAsync();
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> PropertyStatus(int propertyId)
    {
        var status = await _propertyDashboardManager.GetPropertyStatusAsync(propertyId);

        if (status is null)
        {
            return NotFound();
        }

        return Json(status);
    }

    [HttpGet]
    public async Task<IActionResult> PropertyStatement(int propertyId)
    {
        var statement = await _propertyDashboardManager.GetPropertyStatementAsync(propertyId);

        if (statement is null)
        {
            return NotFound();
        }

        return Json(statement);
    }


    [HttpPost]
    public async Task<IActionResult> ParseServicePdf(IFormFile? pdfFile)
    {
        var result = await _propertyDashboardManager.ParseServicePdfAsync(pdfFile);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Json(result);
    }


    [HttpPost]
    public async Task<IActionResult> SaveServices([FromBody] SaveServicesRequestVm request)
    {
        var result = await _propertyDashboardManager.SaveServicesAsync(request);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Json(result);
    }


    [HttpPost]
    public async Task<IActionResult> ParsePaymentPdf(IFormFile? pdfFile, string? descriptionContains)
    {
        var result = await _propertyDashboardManager.ParsePaymentPdfAsync(pdfFile, descriptionContains);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Json(result);
    }


    [HttpPost]
    public async Task<IActionResult> SavePayments([FromBody] SavePaymentsRequestVm request)
    {
        var result = await _propertyDashboardManager.SavePaymentsAsync(request);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Json(result);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
