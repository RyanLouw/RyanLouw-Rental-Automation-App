using Microsoft.AspNetCore.Authorization;
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
