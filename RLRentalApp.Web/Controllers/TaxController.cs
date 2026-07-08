using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RLRentalApp.Web.Managers;

namespace RLRentalApp.Controllers;

[Authorize]
public class TaxController : Controller
{
    private readonly IPropertyDashboardManager _propertyDashboardManager;

    public TaxController(IPropertyDashboardManager propertyDashboardManager)
    {
        _propertyDashboardManager = propertyDashboardManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var vm = await _propertyDashboardManager.GetDashboardAsync();
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Summary(int? year)
    {
        var vm = await _propertyDashboardManager.GetTaxDashboardAsync(year);
        return Json(vm);
    }

    [HttpPost]
    public async Task<IActionResult> SaveBankStatement(IFormFile? document, int year, int month)
    {
        var result = await _propertyDashboardManager.SaveBankStatementAsync(document, year, month);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Json(result);
    }

    [HttpPost]
    public async Task<IActionResult> SaveExpenseDocument(IFormFile? document, int propertyId, int year, int month)
    {
        var result = await _propertyDashboardManager.SaveExpenseDocumentAsync(document, propertyId, year, month);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Json(result);
    }
}
