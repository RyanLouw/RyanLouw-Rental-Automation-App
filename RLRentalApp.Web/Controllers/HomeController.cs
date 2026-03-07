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

        static string Money(decimal value) => $"R {value:N2}";

        var generatedOn = DateTime.Now;
        var dueMonth = new DateTime(statement.StatementMonth.Year, statement.StatementMonth.Month, 1).AddMonths(1);
        var dueDate = new DateTime(dueMonth.Year, dueMonth.Month, 4);

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(column =>
                {
                    column.Spacing(16);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text("MH & Sons").SemiBold().FontSize(20);
                            left.Item().Text("Investment").SemiBold().FontSize(20);
                            left.Item().Text("Properties").SemiBold().FontSize(20);
                        });

                        row.ConstantItem(140).AlignCenter().Text("✦").FontSize(38).FontColor(Colors.Grey.Darken1);

                        row.RelativeItem().AlignRight().Column(right =>
                        {
                            right.Item().Text("Statement")
                                .Italic()
                                .SemiBold()
                                .FontSize(24)
                                .FontColor(Colors.Grey.Darken1);
                        });
                    });

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text("No 9 Waterberg straat");
                            left.Item().Text("Noordheuwel X6");
                            left.Item().Text("Krugersdorp");
                            left.Item().Text("10/4/1904");
                        });

                        row.RelativeItem().Column(right =>
                        {
                            right.Item().Text("Phone: 084 588 4884").SemiBold();
                            right.Item().Text("Fax: 086 507 2111").SemiBold();
                            right.Item().Text("E-mail: hrlouw@justice.gov.za").FontColor(Colors.Blue.Medium);
                        });
                    });

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Date: {generatedOn:MMMM d, yyyy}").SemiBold();
                        row.RelativeItem().AlignRight().Column(right =>
                        {
                            right.Item().Text("Statement To:").SemiBold().FontColor(Colors.Grey.Darken1);
                            right.Item().Text(statement.TenantName).SemiBold().FontSize(13);
                            right.Item().Text(statement.PropertyName).FontSize(13);
                            right.Item().Text(statement.PropertyAddress).FontSize(11).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    column.Item().Text($"Statement month: {statement.StatementMonth:MMMM yyyy}");
                    column.Item().Text($"Current balance: {Money(statement.CurrentBalance)}").SemiBold();

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(85);
                            columns.ConstantColumn(80);
                            columns.RelativeColumn();
                            columns.ConstantColumn(90);
                            columns.ConstantColumn(100);
                        });

                        table.Header(header =>
                        {
                            static IContainer HeaderCell(IContainer container) => container
                                .Background(Colors.Grey.Lighten3)
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
                            table.Cell().Element(BodyCell).AlignRight().Text(Money(entry.Amount));
                            table.Cell().Element(BodyCell).AlignRight().Text(Money(entry.RunningBalance));
                        }
                    });

                    column.Item().PaddingTop(8).Text($"Amount to be paid by {dueDate:dd MMMM yyyy}: {Money(statement.CurrentBalance)}")
                        .SemiBold()
                        .FontSize(13);
                });

                page.Footer().AlignRight().Text(text =>
                {
                    text.Span("Page ");
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
