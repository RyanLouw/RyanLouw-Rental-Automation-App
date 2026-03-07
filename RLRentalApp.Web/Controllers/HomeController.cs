using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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

        var statement = await _propertyDashboardManager.GetPropertyStatementAsync(propertyId, statementMonth);
        if (statement is null)
        {
            return NotFound();
        }

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header()
                    .Column(column =>
                    {
                        column.Item().Text("Tenant Statement")
                            .SemiBold()
                            .FontSize(20)
                            .FontColor(Colors.Blue.Darken3);

                        column.Item().Text($"Generated: {DateTime.Now:dd MMM yyyy}").FontColor(Colors.Grey.Darken1);
                    });

                page.Content().Column(column =>
                {
                    column.Spacing(12);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text($"Property: {statement.PropertyName}").SemiBold();
                            left.Item().Text($"Tenant: {statement.TenantName}");
                            left.Item().Text($"Statement month: {statement.StatementMonth:MMMM yyyy}");
                        });

                        row.RelativeItem().Column(right =>
                        {
                            right.Item().AlignRight().Text($"Opening balance: {statement.OpeningOutstanding:C}");
                            right.Item().AlignRight().Text($"Current balance: {statement.CurrentBalance:C}").SemiBold();
                        });
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.RelativeColumn();
                            columns.ConstantColumn(90);
                            columns.ConstantColumn(100);
                        });

                        table.Header(header =>
                        {
                            static IContainer HeaderCell(IContainer container) => container
                                .Background(Colors.Blue.Lighten4)
                                .PaddingVertical(6)
                                .PaddingHorizontal(8)
                                .BorderBottom(1)
                                .BorderColor(Colors.Grey.Lighten1);

                            header.Cell().Element(HeaderCell).Text("Date").SemiBold();
                            header.Cell().Element(HeaderCell).Text("Type").SemiBold();
                            header.Cell().Element(HeaderCell).Text("Description").SemiBold();
                            header.Cell().Element(HeaderCell).AlignRight().Text("Amount").SemiBold();
                            header.Cell().Element(HeaderCell).AlignRight().Text("Balance").SemiBold();
                        });

                        foreach (var entry in statement.Entries)
                        {
                            static IContainer BodyCell(IContainer container) => container
                                .PaddingVertical(5)
                                .PaddingHorizontal(8)
                                .BorderBottom(1)
                                .BorderColor(Colors.Grey.Lighten3);

                            table.Cell().Element(BodyCell).Text(entry.EntryDate.ToString("dd MMM yyyy"));
                            table.Cell().Element(BodyCell).Text(entry.EntryType);
                            table.Cell().Element(BodyCell).Text(entry.Description);
                            table.Cell().Element(BodyCell).AlignRight().Text(entry.Amount.ToString("C"));
                            table.Cell().Element(BodyCell).AlignRight().Text(entry.RunningBalance.ToString("C"));
                        }
                    });
                });

                page.Footer()
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span("This statement was generated by RL Rental Automation App.");
                        text.Span("  Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
            });
        }).GeneratePdf();

        var fileName = $"tenant-statement-{statement.PropertyName.Replace(' ', '-').ToLowerInvariant()}-{statement.StatementMonth:yyyy-MM}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
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
    public async Task<IActionResult> SavePayments([FromBody] SavePaymentsRequestVm request)
    {
        var result = await _propertyDashboardManager.SavePaymentsAsync(request);

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
