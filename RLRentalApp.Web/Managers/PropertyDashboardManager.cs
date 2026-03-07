using Microsoft.AspNetCore.Http;
using RLRentalApp.Models;
using RLRentalApp.Web.DataAccess;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
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

        var currentMonthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var latestRent = await _dataAccess.LoadLatestRentAsync(activeLease.LeaseId, DateTime.UtcNow.Date);
        var snapshot = await _dataAccess.LoadStatementSnapshotAsync(activeLease.LeaseId, currentMonthStart);
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
            CurrentMonthServiceTotal = snapshot.CurrentMonthServiceTotal,
            CurrentMonthPaymentTotal = snapshot.CurrentMonthPaymentTotal,
            CurrentBalance = openingOutstanding + snapshot.AmountThroughMonth
        };
    }

    public async Task<PropertyStatementVm?> GetPropertyStatementAsync(int propertyId, DateTime? statementMonth = null)
    {
        var property = await _dataAccess.LoadPropertyAsync(propertyId);
        var activeLease = await _dataAccess.LoadActiveLeaseAsync(propertyId);

        if (property is null || activeLease is null)
        {
            return null;
        }

        var openingOutstanding = await _dataAccess.LoadOpeningOutstandingAsync(activeLease.TenantId);
        var monthStart = new DateTime((statementMonth ?? DateTime.UtcNow).Year, (statementMonth ?? DateTime.UtcNow).Month, 1);
        var windowStart = monthStart.AddMonths(-2);
        var statementWindowOpening = openingOutstanding + await _dataAccess.LoadStatementAmountBeforeDateAsync(activeLease.LeaseId, windowStart);
        var snapshot = await _dataAccess.LoadStatementSnapshotAsync(activeLease.LeaseId, monthStart);
        var rawEntries = new List<StatementEntryDataModel>();
        for (var i = 0; i < 3; i++)
        {
            var windowMonth = windowStart.AddMonths(i);
            var monthEntries = await _dataAccess.LoadMonthEntriesAsync(activeLease.LeaseId, windowMonth);
            rawEntries.AddRange(monthEntries);
        }

        var statementEntries = rawEntries
            .OrderBy(x => x.EntryDate)
            .ThenBy(x => x.EntryType)
            .Select(x => new PropertyStatementEntryVm
            {
                StatementEntryId = x.StatementEntryId,
                EntryDate = x.EntryDate,
                EntryType = x.EntryType,
                Description = x.Description,
                Amount = x.Amount,
                CanEdit = string.Equals(x.SourceTable, "payment", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(x.SourceTable, "service_charge", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(x.SourceTable, "rent_rate", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(x.SourceTable, "tenant_deposit", StringComparison.OrdinalIgnoreCase)
            })
            .ToList();

        var runningBalance = statementWindowOpening;
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
            OpeningOutstanding = statementWindowOpening,
            CurrentBalance = openingOutstanding + snapshot.AmountThroughMonth,
            StatementMonth = monthStart,
            Entries = statementEntries
        };
    }




    public async Task<UpdateStatementEntryResultVm> UpdateStatementEntryAsync(UpdateStatementEntryRequestVm request)
    {
        if (request.StatementEntryId <= 0)
        {
            return new UpdateStatementEntryResultVm { Success = false, Message = "Invalid statement row." };
        }

        var activeLease = await _dataAccess.LoadActiveLeaseAsync(request.PropertyId);
        if (activeLease is null)
        {
            return new UpdateStatementEntryResultVm { Success = false, Message = "No active lease found for selected property." };
        }

        return await _dataAccess.UpdateStatementEntryAsync(
            activeLease.LeaseId,
            request.StatementEntryId,
            request.EntryDate,
            request.Amount,
            request.Description ?? string.Empty);
    }


    public async Task<SaveRentResultVm> SaveRentAsync(SaveRentRequestVm request)
    {
        if (request.Amount <= 0)
        {
            return new SaveRentResultVm
            {
                Success = false,
                Message = "Rent amount must be greater than zero."
            };
        }

        var activeLease = await _dataAccess.LoadActiveLeaseAsync(request.PropertyId);
        if (activeLease is null)
        {
            return new SaveRentResultVm
            {
                Success = false,
                Message = "No active lease found for the selected property."
            };
        }

        var effectiveFrom = new DateTime(request.EffectiveFrom.Year, request.EffectiveFrom.Month, 1);
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? "Captured from dashboard" : request.Notes;

        var changed = await _dataAccess.UpsertRentRateAsync(activeLease.LeaseId, effectiveFrom, request.Amount, notes);

        return new SaveRentResultVm
        {
            Success = changed > 0,
            Message = changed > 0
                ? $"Rent saved for {effectiveFrom:yyyy-MM}: {request.Amount:N2}."
                : "No rent changes were saved."
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



    public async Task<SavePaymentsResultVm> SavePaymentsAsync(SavePaymentsRequestVm request)
    {
        var activeLease = await _dataAccess.LoadActiveLeaseAsync(request.PropertyId);
        if (activeLease is null)
        {
            return new SavePaymentsResultVm
            {
                Success = false,
                Message = "No active lease found for the selected property."
            };
        }

        var cleanedPayments = request.Payments
            .Where(x => x.Amount > 0)
            .GroupBy(x => new { Date = x.PaidOn.Date, x.Amount })
            .Select(g => g.First())
            .ToList();

        if (cleanedPayments.Count == 0)
        {
            return new SavePaymentsResultVm
            {
                Success = false,
                Message = "No valid payment items to save."
            };
        }

        var toInsert = new List<PaymentInsertDataModel>();
        var skippedDuplicates = 0;

        foreach (var payment in cleanedPayments)
        {
            var exists = await _dataAccess.PaymentExistsAsync(activeLease.LeaseId, payment.PaidOn, payment.Amount);
            if (exists)
            {
                skippedDuplicates++;
                continue;
            }

            toInsert.Add(new PaymentInsertDataModel
            {
                PaidOn = payment.PaidOn.Date,
                Amount = payment.Amount,
                Reference = payment.Description,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? "Captured from statement PDF" : request.Notes
            });
        }

        var inserted = await _dataAccess.InsertPaymentsAsync(activeLease.LeaseId, toInsert);
        var savedPayments = toInsert
            .Select(x => new PaymentCandidateVm
            {
                PaidOn = x.PaidOn,
                Amount = x.Amount,
                Description = x.Reference
            })
            .ToList();

        return new SavePaymentsResultVm
        {
            Success = inserted > 0,
            AddedCount = inserted,
            SkippedDuplicates = skippedDuplicates,
            SavedPayments = savedPayments,
            Message = inserted > 0
                ? $"Saved {inserted} payment(s). Skipped {skippedDuplicates} duplicate(s)."
                : $"No new payments saved. Skipped {skippedDuplicates} duplicate(s)."
        };
    }

    public async Task<PaymentPdfParseResultVm> ParsePaymentPdfAsync(IFormFile? file, string? descriptionContains)
    {
        if (file is null || file.Length == 0)
        {
            return new PaymentPdfParseResultVm { Success = false, ErrorMessage = "Please choose a PDF file." };
        }

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new PaymentPdfParseResultVm { Success = false, ErrorMessage = "Only PDF files are supported." };
        }

        await using var stream = file.OpenReadStream();
        var text = ExtractPdfText(stream);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new PaymentPdfParseResultVm { Success = false, ErrorMessage = "Could not read text from the PDF." };
        }

        var filter = string.IsNullOrWhiteSpace(descriptionContains) ? "Betaling Van Heino Huur" : descriptionContains.Trim();

        if (filter.Length < 3)
        {
            return new PaymentPdfParseResultVm { Success = false, ErrorMessage = "Search text must be at least 3 characters." };
        }

        var payments = ParsePaymentRows(text, filter);

        return new PaymentPdfParseResultVm
        {
            Success = true,
            Payments = payments,
            RawTextPreview = text.Length > 4000 ? text[..4000] : text
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



    private static List<PaymentCandidateVm> ParsePaymentRows(string text, string descriptionFilter)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var statementYear = InferStatementYear(text);
        var descriptionPattern = BuildLooseDescriptionPattern(descriptionFilter);
        var descriptionMatches = Regex.Matches(text, descriptionPattern, RegexOptions.IgnoreCase);

        var results = new List<PaymentCandidateVm>();

        foreach (Match descriptionMatch in descriptionMatches)
        {
            var backStart = Math.Max(0, descriptionMatch.Index - 120);
            var backWindow = text.Substring(backStart, descriptionMatch.Index - backStart);

            var forwardStart = descriptionMatch.Index + descriptionMatch.Length;
            var forwardWindowLength = Math.Min(140, Math.Max(0, text.Length - forwardStart));
            var forwardWindow = forwardWindowLength > 0 ? text.Substring(forwardStart, forwardWindowLength) : string.Empty;

            var dateMatches = Regex.Matches(backWindow, @"(?i)(?<!\d)(\d{1,2})\s*(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)");
            if (dateMatches.Count == 0)
            {
                continue;
            }

            var dateToken = dateMatches[^1].Value;
            if (!TryParseStatementDate(dateToken, statementYear, out var paidOn))
            {
                continue;
            }

            var amountMatch = Regex.Match(forwardWindow, @"(-?\d{1,3}(?:,\d{3})*(?:\.\d{2})|-?\d+(?:\.\d{2}))\s*(?:Kt|CT|Dt|Cr|Dr)?");
            if (!amountMatch.Success)
            {
                continue;
            }

            var amount = TryParseDecimal(amountMatch.Groups[1].Value);
            if (!amount.HasValue || amount.Value <= 0)
            {
                continue;
            }

            results.Add(new PaymentCandidateVm
            {
                PaidOn = paidOn.Date,
                Amount = amount.Value,
                Description = descriptionFilter
            });
        }

        return results
            .GroupBy(x => new { Date = x.PaidOn.Date, x.Amount })
            .Select(g => g.First())
            .OrderBy(x => x.PaidOn)
            .ThenBy(x => x.Amount)
            .ToList();
    }

    private static string BuildLooseDescriptionPattern(string description)
    {
        var tokens = description
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Regex.Escape)
            .ToList();

        if (tokens.Count == 0)
        {
            return Regex.Escape(description);
        }

        return string.Join(@"\W*", tokens);
    }


    private static int InferStatementYear(string text)
    {
        var explicitYear = Regex.Match(text, @"(?is)Staatdatum\s*:\s*\d{1,2}\s+[A-Za-z]+\s+(20\d{2})");
        if (explicitYear.Success && int.TryParse(explicitYear.Groups[1].Value, out var stateYear))
        {
            return stateYear;
        }

        var periodYear = Regex.Match(text, @"(?is)Staat\s*Periode\s*:\s*\d{1,2}\s+[A-Za-z]+\s+(20\d{2})\s+tot\s+\d{1,2}\s+[A-Za-z]+\s+(20\d{2})");
        if (periodYear.Success)
        {
            var first = int.TryParse(periodYear.Groups[1].Value, out var y1) ? y1 : 0;
            var second = int.TryParse(periodYear.Groups[2].Value, out var y2) ? y2 : 0;

            if (first >= 2000 && first <= 2100)
            {
                return first;
            }

            if (second >= 2000 && second <= 2100)
            {
                return second;
            }
        }

        var years = Regex.Matches(text, @"(?<!\d)(20\d{2})(?![\d,.])")
            .Cast<Match>()
            .Select(x => int.TryParse(x.Groups[1].Value, out var value) ? value : 0)
            .Where(x => x >= 2000 && x <= 2100)
            .ToList();

        return years.Count > 0 ? years.Max() : DateTime.UtcNow.Year;
    }

    private static bool TryParseStatementDate(string dateToken, int year, out DateTime date)
    {
        var composed = $"{dateToken} {year}";

        return DateTime.TryParseExact(composed, "d MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
               || DateTime.TryParseExact(composed, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
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
