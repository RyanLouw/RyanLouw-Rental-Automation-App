using Microsoft.AspNetCore.Http;
using RLRentalApp.Models;
using RLRentalApp.Web.DataAccess;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace RLRentalApp.Web.Managers;

public class PropertyDashboardManager : IPropertyDashboardManager
{
    private readonly IPropertyDashboardDataAccess _dataAccess;

    public PropertyDashboardManager(IPropertyDashboardDataAccess dataAccess)
    {
        _dataAccess = dataAccess;
    }

    public async Task<HomeIndexVm> GetDashboardAsync()
    {
        var properties = await _dataAccess.LoadPropertiesAsync();

        return new HomeIndexVm
        {
            Properties = properties
        };
    }

    public async Task<PropertyStatusVm?> GetPropertyStatusAsync(int propertyId)
    {
        var property = await _dataAccess.LoadPropertyAsync(propertyId);
        if (property is null)
        {
            return null;
        }

        var activeLease = await _dataAccess.LoadActiveLeaseAsync(propertyId);
        if (activeLease is null)
        {
            return new PropertyStatusVm
            {
                PropertyId = property.Id,
                PropertyName = property.Name,
                PropertyAddress = property.AddressLine1,
                IsPropertyActive = property.IsActive,
                HasActiveLease = false
            };
        }

        var latestRent = await _dataAccess.LoadLatestRentAsync(activeLease.LeaseId);
        var serviceTotal = await _dataAccess.LoadCurrentMonthServiceTotalAsync(activeLease.LeaseId);
        var paymentTotal = await _dataAccess.LoadCurrentMonthPaymentTotalAsync(activeLease.LeaseId);
        var openingOutstanding = await _dataAccess.LoadOpeningOutstandingAsync(activeLease.TenantId);

        return new PropertyStatusVm
        {
            PropertyId = property.Id,
            PropertyName = property.Name,
            PropertyAddress = property.AddressLine1,
            IsPropertyActive = property.IsActive,
            HasActiveLease = true,
            LeaseId = activeLease.LeaseId,
            TenantId = activeLease.TenantId,
            TenantName = activeLease.TenantName,
            LeaseStartDate = activeLease.StartDate,
            LatestRent = latestRent,
            OpeningOutstanding = openingOutstanding,
            CurrentMonthServiceTotal = serviceTotal,
            CurrentMonthPaymentTotal = paymentTotal,
            CurrentBalance = openingOutstanding + (latestRent ?? 0m) + serviceTotal - paymentTotal
        };
    }

    public async Task<PropertyStatementVm?> GetPropertyStatementAsync(int propertyId)
    {
        var property = await _dataAccess.LoadPropertyAsync(propertyId);
        var activeLease = await _dataAccess.LoadActiveLeaseAsync(propertyId);

        if (property is null || activeLease is null)
        {
            return null;
        }

        var openingOutstanding = await _dataAccess.LoadOpeningOutstandingAsync(activeLease.TenantId);
        var latestRent = await _dataAccess.LoadLatestRentAsync(activeLease.LeaseId);
        var rawEntries = await _dataAccess.LoadCurrentMonthEntriesAsync(activeLease.LeaseId);

        var statementEntries = rawEntries
            .Select(x => new PropertyStatementEntryVm
            {
                EntryDate = x.EntryDate,
                EntryType = x.EntryType,
                Description = x.Description,
                Amount = x.Amount
            })
            .ToList();

        if (latestRent.HasValue)
        {
            statementEntries.Add(new PropertyStatementEntryVm
            {
                EntryDate = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1),
                EntryType = "Rent",
                Description = "Current month rent",
                Amount = latestRent.Value
            });
        }

        statementEntries = statementEntries
            .OrderBy(x => x.EntryDate)
            .ThenBy(x => x.EntryType)
            .ToList();

        var runningBalance = openingOutstanding;
        foreach (var entry in statementEntries)
        {
            runningBalance += entry.Amount;
            entry.RunningBalance = runningBalance;
        }

        return new PropertyStatementVm
        {
            PropertyId = property.Id,
            LeaseId = activeLease.LeaseId,
            TenantId = activeLease.TenantId,
            PropertyName = property.Name,
            TenantName = activeLease.TenantName,
            OpeningOutstanding = openingOutstanding,
            CurrentBalance = runningBalance,
            Entries = statementEntries
        };
    }


    public async Task<SaveServicesResultVm> SaveServicesAsync(SaveServicesRequestVm request)
    {
        var activeLease = await _dataAccess.LoadActiveLeaseAsync(request.PropertyId);
        if (activeLease is null)
        {
            return new SaveServicesResultVm
            {
                Success = false,
                Message = "No active lease found for the selected property."
            };
        }

        var charges = new List<ServiceChargeInsertDataModel>();

        AddCharge(charges, "Electricity", request.ElectricityAmount, request.BillingPeriod, request.Notes);
        AddCharge(charges, "Water", request.WaterAmount, request.BillingPeriod, request.Notes);
        AddCharge(charges, "Sanitation", request.SewerageAmount, request.BillingPeriod, request.Notes);
        AddCharge(charges, "Refuse", request.RefuseAmount, request.BillingPeriod, request.Notes);

        if (charges.Count == 0)
        {
            return new SaveServicesResultVm
            {
                Success = false,
                Message = "Please capture at least one service amount."
            };
        }

        var inserted = await _dataAccess.InsertServiceChargesAsync(activeLease.LeaseId, charges);

        return new SaveServicesResultVm
        {
            Success = inserted > 0,
            AddedCount = inserted,
            Message = inserted > 0 ? $"Saved {inserted} service charge(s)." : "No service charges were saved."
        };
    }

    public async Task<ServicePdfParseResultVm> ParseServicePdfAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            return new ServicePdfParseResultVm { Success = false, ErrorMessage = "Please choose a PDF file." };
        }

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new ServicePdfParseResultVm { Success = false, ErrorMessage = "Only PDF files are supported." };
        }

        await using var stream = file.OpenReadStream();
        var text = ExtractPdfText(stream);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new ServicePdfParseResultVm { Success = false, ErrorMessage = "Could not read text from the PDF." };
        }

        var electricity = ParseElectricity(text);
        var water = ParseWater(text);
        var sewerage = ParseSewerage(text);
        var refuse = ParseRefuse(text);

        return new ServicePdfParseResultVm
        {
            Success = true,
            Electricity = electricity,
            Water = water,
            Sewerage = sewerage,
            Refuse = refuse,
            RawTextPreview = text.Length > 4000 ? text[..4000] : text
        };
    }


    private static void AddCharge(List<ServiceChargeInsertDataModel> charges, string serviceType, decimal? amount, DateTime billingPeriod, string notes)
    {
        if (!amount.HasValue || amount.Value <= 0)
        {
            return;
        }

        charges.Add(new ServiceChargeInsertDataModel
        {
            ServiceTypeName = serviceType,
            Amount = amount.Value,
            BillingPeriod = new DateTime(billingPeriod.Year, billingPeriod.Month, 1),
            Notes = string.IsNullOrWhiteSpace(notes) ? "Captured from dashboard" : notes
        });
    }

    private static string ExtractPdfText(Stream stream)
    {
        var builder = new StringBuilder();
        using var document = PdfDocument.Open(stream);

        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        return builder.ToString();
    }

    private static ElectricityParseVm ParseElectricity(string text)
    {
        var meter = ParseMeterReadingByType(text, "ELECTRICITY");

        return new ElectricityParseVm
        {
            OldReading = meter.OldReading,
            NewReading = meter.NewReading,
            LeviedAmount = meter.LeviedAmount
        };
    }

    private static WaterParseVm ParseWater(string text)
    {
        var meter = ParseMeterReadingByType(text, "WATER");

        return new WaterParseVm
        {
            OldReading = meter.OldReading,
            NewReading = meter.NewReading,
            LeviedAmount = meter.LeviedAmount
        };
    }


    private static RefuseParseVm ParseRefuse(string text)
    {
        var refuse = ParseAccountChargeByKeyword(text, "REFUSE");

        return new RefuseParseVm
        {
            Date = refuse.Date,
            Code = refuse.Code,
            AmountInclVat = refuse.Amount
        };
    }

    private static SewerageParseVm ParseSewerage(string text)
    {
        var sewerage = ParseAccountChargeByKeyword(text, "SEWERAGE|SEWER|SANITATION");

        return new SewerageParseVm
        {
            Date = sewerage.Date,
            Code = sewerage.Code,
            AmountInclVat = sewerage.Amount
        };
    }

    private sealed class MeterReadResult
    {
        public decimal? OldReading { get; set; }
        public decimal? NewReading { get; set; }
        public decimal? LeviedAmount { get; set; }
    }

    private sealed class AccountChargeResult
    {
        public string Date { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public decimal? Amount { get; set; }
    }

    private static MeterReadResult ParseMeterReadingByType(string text, string meterType)
    {
        var result = new MeterReadResult();
        var meterSection = GetSection(text, "METER READINGS", "ACCOUNT DETAILS");

        var rowMatches = Regex.Matches(
            meterSection,
            $@"(?is){meterType}\s*(\d+\.\d{{3}})\s*(\d+\.\d{{3}})\s*I?\s*(\d*\.\d{{3}})\s*([\d,]+\.\d{{2}})");

        if (rowMatches.Count > 0)
        {
            var selected = rowMatches
                .Cast<Match>()
                .FirstOrDefault(m => (TryParseDecimal(m.Groups[3].Value) ?? 0m) > 0)
                ?? rowMatches[0];

            result.OldReading = TryParseDecimal(selected.Groups[1].Value);
            result.NewReading = TryParseDecimal(selected.Groups[2].Value);
            result.LeviedAmount = TryParseDecimal(selected.Groups[4].Value);
            return result;
        }

        var oldMatch = Regex.Match(text, $@"(?i){meterType}[\s\S]{{0,120}}?old\s*read(?:ing)?\s*[:\-]?\s*(\d+(?:\.\d+)?)");
        var newMatch = Regex.Match(text, $@"(?i){meterType}[\s\S]{{0,120}}?new\s*read(?:ing)?\s*[:\-]?\s*(\d+(?:\.\d+)?)");
        var leviedMatch = Regex.Match(text, $@"(?i){meterType}[\s\S]{{0,200}}?(levied\s*amount|amount\s*incl\s*vat|amount)\s*[:\-]?\s*(?:R)?\s*([\d,]+(?:\.\d{{1,2}})?)");

        result.OldReading = TryParseDecimal(oldMatch.Groups[1].Value);
        result.NewReading = TryParseDecimal(newMatch.Groups[1].Value);
        result.LeviedAmount = TryParseDecimal(leviedMatch.Groups[2].Value);

        return result;
    }

    private static AccountChargeResult ParseAccountChargeByKeyword(string text, string keywordPattern)
    {
        var result = new AccountChargeResult();
        var accountSection = GetSection(text, "ACCOUNT DETAILS", string.Empty);
        var keywordMatches = Regex.Matches(accountSection, $@"(?i)({keywordPattern})");

        if (keywordMatches.Count > 0)
        {
            decimal total = 0m;

            foreach (Match keyword in keywordMatches)
            {
                var keywordIndex = keyword.Index;
                var backStart = Math.Max(0, keywordIndex - 60);
                var backWindow = accountSection.Substring(backStart, keywordIndex - backStart);
                var dateCodeMatches = Regex.Matches(backWindow, @"(\d{2}[\-/]\d{2}[\-/]\d{4})(\d{4,8})");
                var dateCode = dateCodeMatches.Count > 0 ? dateCodeMatches[^1] : null;

                if (string.IsNullOrWhiteSpace(result.Date) && dateCode is not null)
                {
                    result.Date = dateCode.Groups[1].Value;
                    result.Code = dateCode.Groups[2].Value;
                }

                var forward = accountSection.Substring(keywordIndex, Math.Min(260, accountSection.Length - keywordIndex));
                var nextDate = Regex.Match(forward[1..], @"\d{2}[\-/]\d{2}[\-/]\d{4}");
                var chunk = nextDate.Success ? forward[..(nextDate.Index + 1)] : forward;

                var decimalTokens = Regex.Matches(chunk, @"-?\d+(?:,\d{3})*(?:\.\d{2})")
                    .Cast<Match>()
                    .Select(m => TryParseDecimal(m.Value))
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                if (decimalTokens.Count > 0)
                {
                    total += decimalTokens[^1];
                }
            }

            if (total != 0m)
            {
                result.Amount = total;
                return result;
            }
        }

        var block = Regex.Match(text, $@"(?is)({keywordPattern})[\s\S]{{0,420}}").Value;
        if (!string.IsNullOrWhiteSpace(block))
        {
            var dateMatch = Regex.Match(block, @"\b(\d{4}[\-/]\d{2}[\-/]\d{2}|\d{2}[\-/]\d{2}[\-/]\d{4})\b");
            var codeMatch = Regex.Match(block, @"(?i)code\s*[:\-]?\s*([A-Za-z0-9\-]+)");
            var amountMatch = Regex.Match(block, @"(?i)(amount\s*incl\s*vat|incl\s*vat|amount)\s*[:\-]?\s*(?:R)?\s*([\d,]+(?:\.\d{1,2})?)");

            result.Date = dateMatch.Groups[1].Value;
            result.Code = codeMatch.Groups[1].Value;
            result.Amount = TryParseDecimal(amountMatch.Groups[2].Value);
        }

        return result;
    }

    private static string GetSection(string fullText, string startMarker, string endMarker)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return string.Empty;
        }

        var startIndex = fullText.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return fullText;
        }

        var remaining = fullText[startIndex..];

        if (string.IsNullOrWhiteSpace(endMarker))
        {
            return remaining;
        }

        var endIndex = remaining.IndexOf(endMarker, startMarker.Length, StringComparison.OrdinalIgnoreCase);
        return endIndex > 0 ? remaining[..endIndex] : remaining;
    }

    private static decimal? TryParseDecimal(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var normalized = input.Replace(",", "").Trim();

        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
