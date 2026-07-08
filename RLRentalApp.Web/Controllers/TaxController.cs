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
    public async Task<IActionResult> Transactions(int? year)
    {
        var vm = await _propertyDashboardManager.GetTaxTransactionsAsync(year);
        return Json(vm);
    }

    [HttpPost]
    public async Task<IActionResult> SaveTransaction(IFormFile? proofFile, int propertyId, DateTime transactionDate, string entryKind, decimal amount, string description)
    {
        var result = await _propertyDashboardManager.SaveTaxTransactionAsync(proofFile, propertyId, transactionDate, entryKind, amount, description);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Json(result);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteTransaction(long taxTransactionId, bool deleteProofFile = true)
    {
        var result = await _propertyDashboardManager.DeleteTaxTransactionAsync(taxTransactionId, deleteProofFile);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Json(result);
    }

}
