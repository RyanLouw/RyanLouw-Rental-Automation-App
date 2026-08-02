using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RLRentalApp.Models;
using RLRentalApp.Web.Managers;
using RLRentalApp.Web.Services;
using System.Diagnostics;

namespace RLRentalApp.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IPropertyDashboardManager _propertyDashboardManager;
    private readonly IGoogleDriveStorageService _googleDriveStorageService;

    public HomeController(
        ILogger<HomeController> logger,
        IPropertyDashboardManager propertyDashboardManager,
        IGoogleDriveStorageService googleDriveStorageService)
    {
        _logger = logger;
        _propertyDashboardManager = propertyDashboardManager;
        _googleDriveStorageService = googleDriveStorageService;
    }

    public async Task<IActionResult> Index()
    {
        var vm = await _propertyDashboardManager.GetDashboardAsync();
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> TestGoogleDrive()
    {
        try
        {
            var result = await _googleDriveStorageService.TestConnectionAsync();
            return Json(result);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Google Drive connection test failed.");
            return BadRequest(new { message = GetGoogleDriveConnectionErrorMessage(exception) });
        }
    }

    private static string GetGoogleDriveConnectionErrorMessage(Exception exception)
    {
        return exception switch
        {
            InvalidOperationException => exception.Message,
            _ when exception.Message.Contains("File not found", StringComparison.OrdinalIgnoreCase)
                => "Google Drive could not find the configured folder. Check GoogleDrive:FolderId and make sure the connected Google account can edit that folder.",
            _ when exception.Message.Contains("permission", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
                => "Google Drive denied access. Sign in with the Google account that owns the folder or has Editor access.",
            _ => "Google Drive could not create the test folder and file. Check that the Google Drive API is enabled, then check the application log for the technical error."
        };
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
    public async Task<IActionResult> PropertyStatement(int propertyId, string? month)
    {
        DateTime? statementMonth = null;
        if (!string.IsNullOrWhiteSpace(month) && DateTime.TryParse($"{month}-01", out var parsedMonth))
        {
            statementMonth = parsedMonth;
        }

        var statement = await _propertyDashboardManager.GetPropertyStatementAsync(propertyId, statementMonth);

        if (statement is null)
        {
            return NotFound();
        }

        return Json(statement);
    }

    [HttpGet]
    public async Task<IActionResult> PropertyStatementPdf(int propertyId, string? month)
    {
        DateTime? statementMonth = null;
        if (!string.IsNullOrWhiteSpace(month) && DateTime.TryParse($"{month}-01", out var parsedMonth))
        {
            statementMonth = parsedMonth;
        }

        var statementPdf = await _propertyDashboardManager.GeneratePropertyStatementPdfAsync(propertyId, statementMonth);
        if (statementPdf is null)
        {
            return NotFound();
        }

        return File(statementPdf.PdfBytes, "application/pdf", statementPdf.FileName);
    }


    [HttpPost]
    public async Task<IActionResult> ParseServicePdf(IFormFile? pdfFile, string? pdfPassword, int propertyId)
    {
        var result = await _propertyDashboardManager.ParseServicePdfAsync(pdfFile, pdfPassword);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        var property = await _propertyDashboardManager.GetPropertyStatusAsync(propertyId);
        if (property is null)
        {
            result.DocumentMessage = "The PDF was parsed, but it was not saved to Google Drive because the property could not be found.";
            return Json(result);
        }

        try
        {
            await UploadDriveDocumentAsync(pdfFile!, ["Properties", SafeFolderName(property.PropertyName), DateTime.UtcNow.Year.ToString(), DateTime.UtcNow.ToString("MM")]);
            result.DocumentUploaded = true;
            result.DocumentMessage = "Services PDF saved to Google Drive successfully.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The services PDF was parsed but its Google Drive upload failed.");
            result.DocumentMessage = $"The PDF was parsed, but it was not saved to Google Drive: {GetGoogleDriveConnectionErrorMessage(exception)}";
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
    public async Task<IActionResult> SaveRent([FromBody] SaveRentRequestVm request)
    {
        var result = await _propertyDashboardManager.SaveRentAsync(request);

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
    public async Task<IActionResult> ParseAllRentersPaymentPdf(IFormFile? pdfFile)
    {
        var result = await _propertyDashboardManager.ParseAllRentersPaymentPdfAsync(pdfFile);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Json(result);
    }


    [HttpPost]
    public async Task<IActionResult> SavePayments(string requestJson, IFormFile? pdfFile)
    {
        var request = System.Text.Json.JsonSerializer.Deserialize<SavePaymentsRequestVm>(requestJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (request is null) return BadRequest(new { message = "Could not read the payment details." });

        var result = await _propertyDashboardManager.SavePaymentsAsync(request);
        if (!result.Success) return BadRequest(result);

        if (pdfFile is not null && pdfFile.Length > 0)
        {
            try
            {
                var month = result.SavedPayments.FirstOrDefault()?.PaidOn ?? DateTime.UtcNow;
                await UploadDriveDocumentAsync(pdfFile, ["Bank", month.Year.ToString(), month.ToString("MM")]);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Payment values were saved but the bank PDF upload failed.");
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = $"Payment values were saved, but the bank PDF was not uploaded: {GetGoogleDriveConnectionErrorMessage(exception)}",
                    savedPayments = result.SavedPayments,
                    documentUploaded = false
                });
            }
        }

        return Json(new
        {
            result.Success,
            result.Message,
            result.AddedCount,
            result.SkippedDuplicates,
            result.SavedPayments,
            result.RenterMatches,
            documentUploaded = pdfFile is not null && pdfFile.Length > 0,
            documentMessage = pdfFile is not null && pdfFile.Length > 0
                ? "Bank statement PDF saved to Google Drive successfully."
                : "Payment values were saved. No bank statement PDF was selected for upload."
        });
    }



    [HttpPost]
    public async Task<IActionResult> SaveManualLateCharge([FromBody] ManualLateChargeRequestVm request)
    {
        var result = await _propertyDashboardManager.SaveManualLateChargeAsync(request);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Json(result);
    }


    [HttpPost]
    public async Task<IActionResult> SendTenantEmail([FromBody] SendTenantEmailRequestVm request)
    {
        var result = await _propertyDashboardManager.SendTenantEmailAsync(request);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Json(result);
    }


    [HttpPost]
    public async Task<IActionResult> UpdateStatementEntry([FromBody] UpdateStatementEntryRequestVm request)
    {
        var result = await _propertyDashboardManager.UpdateStatementEntryAsync(request);

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Json(result);
    }

    private async Task UploadDriveDocumentAsync(IFormFile pdfFile, IReadOnlyList<string> folderNames)
    {
        await using var stream = new MemoryStream();
        await pdfFile.CopyToAsync(stream);
        await _googleDriveStorageService.UploadFileToFoldersAsync(pdfFile.FileName, stream.ToArray(), pdfFile.ContentType ?? "application/pdf", folderNames);
    }

    private static string SafeFolderName(string name) => string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim();

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
