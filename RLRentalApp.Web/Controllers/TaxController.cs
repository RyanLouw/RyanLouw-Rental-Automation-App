using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RLRentalApp.Models;
using RLRentalApp.Web.Data;
using System.Data.Common;
using System.Text.Json;

namespace RLRentalApp.Controllers;

[Authorize]
public class TaxController : Controller
{
    private readonly AuthDbContext _dbContext;

    public TaxController(AuthDbContext dbContext) => _dbContext = dbContext;

    [HttpGet]
    public async Task<IActionResult> Index(int? propertyId, DateTime? fromMonth, DateTime? toMonth)
    {
        var now = DateTime.UtcNow;
        var from = FirstOfMonth(fromMonth ?? new DateTime(now.Year, 1, 1));
        var to = FirstOfMonth(toMonth ?? now);
        var vm = new TaxReportVm
        {
            Properties = await LoadPropertiesAsync(),
            PropertyId = propertyId,
            FromMonth = from,
            ToMonth = to
        };

        if (propertyId is > 0 && from <= to)
            await LoadReportDataAsync(vm);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult GeneratePdf(string reportJson)
    {
        var report = JsonSerializer.Deserialize<TaxReportDocumentVm>(reportJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (report is null || report.PropertyId <= 0 || report.Months.Count == 0 || report.Months.Count > 120)
            return BadRequest("The tax report could not be read.");

        foreach (var section in report.Sections)
        foreach (var row in section.Rows)
            if (row.Values.Count != report.Months.Count)
                return BadRequest("Every report row must contain one value for every month.");

        var pdf = BuildPdf(report);
        var safeName = string.Concat(report.PropertyName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return File(pdf, "application/pdf", $"Tax report - {safeName} - {report.FromMonth} to {report.ToMonth}.pdf");
    }

    private async Task LoadReportDataAsync(TaxReportVm vm)
    {
        var property = vm.Properties.FirstOrDefault(x => x.Id == vm.PropertyId);
        if (property is null) return;

        vm.PropertyName = property.Name;
        for (var month = vm.FromMonth; month <= vm.ToMonth; month = month.AddMonths(1))
            vm.Months.Add(month.ToString("yyyy-MM"));

        var connection = _dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT entry_date, description, amount
            FROM property_tax_entry
            WHERE property_id = @propertyId
              AND entry_date >= @fromDate
              AND entry_date < @toDate
            ORDER BY entry_date, description;";
        AddParameter(command, "@propertyId", vm.PropertyId!.Value);
        AddParameter(command, "@fromDate", vm.FromMonth.Date);
        AddParameter(command, "@toDate", vm.ToMonth.AddMonths(1).Date);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                vm.Entries.Add(new TaxLedgerEntryVm
                {
                    EntryDate = reader.GetDateTime(0),
                    Description = reader.GetString(1),
                    Amount = reader.GetDecimal(2)
                });
            }
        }

        await using var descriptions = connection.CreateCommand();
        descriptions.CommandText = @"
            SELECT DISTINCT description
            FROM property_tax_entry
            WHERE property_id = @propertyId
            ORDER BY description;";
        AddParameter(descriptions, "@propertyId", vm.PropertyId.Value);
        await using var descriptionReader = await descriptions.ExecuteReaderAsync();
        while (await descriptionReader.ReadAsync()) vm.Descriptions.Add(descriptionReader.GetString(0));
    }

    private async Task<List<PropertyOptionVm>> LoadPropertiesAsync()
    {
        var result = new List<PropertyOptionVm>();
        var connection = _dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, COALESCE(address_line1, ''), COALESCE(address_line2, ''), is_active FROM property ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new PropertyOptionVm { Id = reader.GetInt32(0), Name = reader.GetString(1), AddressLine1 = reader.GetString(2), AddressLine2 = reader.GetString(3), IsActive = reader.GetBoolean(4) });
        return result;
    }

    private static byte[] BuildPdf(TaxReportDocumentVm report) => Document.Create(document =>
    {
        document.Page(page =>
        {
            page.Size(PageSizes.A3.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(x => x.FontSize(report.Months.Count > 24 ? 6 : 8));
            page.Header().Column(header =>
            {
                header.Item().Text("Property Tax Report").Bold().FontSize(18).FontColor(Colors.Blue.Darken2);
                header.Item().Text($"{report.PropertyName}  |  {report.FromMonth} to {report.ToMonth}").FontSize(10);
            });
            page.Content().PaddingVertical(12).Column(content =>
            {
                content.Spacing(14);
                foreach (var section in report.Sections)
                {
                    content.Item().Text(section.Name).Bold().FontSize(13);
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2.5f);
                            foreach (var _ in report.Months) columns.RelativeColumn();
                            columns.RelativeColumn(1.2f);
                        });
                        table.Header(header =>
                        {
                            Cell(header.Cell()).Text("Description").Bold();
                            foreach (var month in report.Months) Cell(header.Cell()).AlignRight().Text(month).Bold();
                            Cell(header.Cell()).AlignRight().Text("Total").Bold();
                        });
                        foreach (var row in section.Rows)
                        {
                            Cell(table.Cell()).Text(row.Description);
                            foreach (var value in row.Values) Cell(table.Cell()).AlignRight().Text(value.ToString("N2"));
                            Cell(table.Cell()).AlignRight().Text(row.Values.Sum().ToString("N2")).Bold();
                        }
                        Cell(table.Cell()).Text("TOTAL").Bold();
                        for (var i = 0; i < report.Months.Count; i++) Cell(table.Cell()).AlignRight().Text(section.Rows.Sum(r => r.Values[i]).ToString("N2")).Bold();
                        Cell(table.Cell()).AlignRight().Text(section.Rows.Sum(r => r.Values.Sum()).ToString("N2")).Bold();
                    });
                }
            });
            page.Footer().AlignCenter().Text(text => { text.Span("Generated "); text.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm")); text.Span("  •  Page "); text.CurrentPageNumber(); });
        });
    }).GeneratePdf();

    private static IContainer Cell(IContainer container) => container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3);
    private static DateTime FirstOfMonth(DateTime date) => new(date.Year, date.Month, 1);
    private static async Task EnsureOpenAsync(DbConnection connection) { if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(); }
    private static void AddParameter(DbCommand command, string name, object value) { var p = command.CreateParameter(); p.ParameterName = name; p.Value = value; command.Parameters.Add(p); }
}
