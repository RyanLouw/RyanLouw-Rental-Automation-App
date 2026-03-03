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
        var sewerage = ParseSewerage(text);

        return new ServicePdfParseResultVm
        {
            Success = true,
            Electricity = electricity,
            Sewerage = sewerage,
            RawTextPreview = text.Length > 4000 ? text[..4000] : text
        };
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
        var result = new ElectricityParseVm();

        var meterSection = GetSection(text, "METER READINGS", "ACCOUNT DETAILS");

        // Table-style row pattern (works for many municipality statement layouts)
        var rowMatches = Regex.Matches(
            meterSection,
            @"(?is)ELECTRICITY\s*(\d+\.\d{3})\s*(\d+\.\d{3})\s*I?\s*(\d*\.\d{3})\s*([\d,]+\.\d{2})");

        if (rowMatches.Count > 0)
        {
            // Prefer a row with actual usage, otherwise first row.
            var selected = rowMatches
                .Cast<Match>()
                .FirstOrDefault(m => (TryParseDecimal(m.Groups[3].Value) ?? 0m) > 0)
                ?? rowMatches[0];

            result.OldReading = TryParseDecimal(selected.Groups[1].Value);
            result.NewReading = TryParseDecimal(selected.Groups[2].Value);
            result.LeviedAmount = TryParseDecimal(selected.Groups[4].Value);

            return result;
        }

        // Fallback for non-tabular/OCR text
        var oldMatch = Regex.Match(text, @"(?i)electricity[\s\S]{0,120}?old\s*read(?:ing)?\s*[:\-]?\s*(\d+(?:\.\d+)?)");
        var newMatch = Regex.Match(text, @"(?i)electricity[\s\S]{0,120}?new\s*read(?:ing)?\s*[:\-]?\s*(\d+(?:\.\d+)?)");
        var leviedMatch = Regex.Match(text, @"(?i)electricity[\s\S]{0,200}?(levied\s*amount|amount\s*incl\s*vat|amount)\s*[:\-]?\s*(?:R)?\s*([\d,]+(?:\.\d{1,2})?)");

        result.OldReading = TryParseDecimal(oldMatch.Groups[1].Value);
        result.NewReading = TryParseDecimal(newMatch.Groups[1].Value);
        result.LeviedAmount = TryParseDecimal(leviedMatch.Groups[2].Value);

        return result;
    }

    private static SewerageParseVm ParseSewerage(string text)
    {
        var result = new SewerageParseVm();

        var accountSection = GetSection(text, "ACCOUNT DETAILS", string.Empty);
        var keywordMatches = Regex.Matches(accountSection, @"(?i)(SEWERAGE|SEWER|SANITATION)");

        if (keywordMatches.Count > 0)
        {
            decimal total = 0m;

            foreach (Match keyword in keywordMatches)
            {
                var keywordIndex = keyword.Index;

                // Try to read the date+code immediately before this sewerage phrase.
                var backStart = Math.Max(0, keywordIndex - 60);
                var backWindow = accountSection.Substring(backStart, keywordIndex - backStart);
                var dateCodeMatches = Regex.Matches(backWindow, @"(\d{2}[\-/]\d{2}[\-/]\d{4})(\d{4,8})");
                var dateCode = dateCodeMatches.Count > 0 ? dateCodeMatches[^1] : null;

                if (string.IsNullOrWhiteSpace(result.Date) && dateCode is not null)
                {
                    result.Date = dateCode.Groups[1].Value;
                    result.Code = dateCode.Groups[2].Value;
                }

                // Read forward until next date row (or a bounded length), then extract amount tokens.
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
                result.AmountInclVat = total;
                return result;
            }
        }

        // Fallback pattern around sewerage keywords.
        var block = Regex.Match(text, @"(?is)(sewerage|sewer|sanitation)[\s\S]{0,420}").Value;
        if (string.IsNullOrWhiteSpace(block))
        {
            return result;
        }

        var dateMatch = Regex.Match(block, @"\b(\d{4}[\-/]\d{2}[\-/]\d{2}|\d{2}[\-/]\d{2}[\-/]\d{4})\b");
        var codeMatch = Regex.Match(block, @"(?i)code\s*[:\-]?\s*([A-Za-z0-9\-]+)");
        var amountMatch = Regex.Match(block, @"(?i)(amount\s*incl\s*vat|incl\s*vat|amount)\s*[:\-]?\s*(?:R)?\s*([\d,]+(?:\.\d{1,2})?)");

        result.Date = dateMatch.Groups[1].Value;
        result.Code = codeMatch.Groups[1].Value;
        result.AmountInclVat = TryParseDecimal(amountMatch.Groups[2].Value);

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
