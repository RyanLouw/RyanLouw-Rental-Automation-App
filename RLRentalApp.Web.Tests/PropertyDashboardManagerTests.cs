using RLRentalApp.Models;
using RLRentalApp.Web.DataAccess;
using RLRentalApp.Web.Managers;
using RLRentalApp.Web.Services;
using QuestPDF.Infrastructure;
using Xunit;

namespace RLRentalApp.Web.Tests;

public class PropertyDashboardManagerTests
{
    public PropertyDashboardManagerTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public async Task GetPropertyStatusAsync_UsesLedgerSnapshotForCurrentBalance()
    {
        var dataAccess = new FakePropertyDashboardDataAccess
        {
            Property = new PropertyOptionVm { Id = 7, Name = "House", AddressLine1 = "123 Main", IsActive = true },
            ActiveLease = new ActiveLeaseDataModel { LeaseId = 9, TenantId = 13, TenantName = "Tenant", StartDate = new DateTime(2024, 1, 1) },
            OpeningOutstanding = 1000m,
            LatestRent = 4500m,
            Snapshot = new StatementSnapshotDataModel
            {
                AmountThroughMonth = 250m,
                CurrentMonthServiceTotal = 300m,
                CurrentMonthPaymentTotal = 50m
            }
        };

        var sut = new PropertyDashboardManager(dataAccess, new FakeEmailService());

        var status = await sut.GetPropertyStatusAsync(7);

        Assert.NotNull(status);
        Assert.Equal(1250m, status!.CurrentBalance);
        Assert.Equal(300m, status.CurrentMonthServiceTotal);
        Assert.Equal(50m, status.CurrentMonthPaymentTotal);
        Assert.Equal(4500m, status.LatestRent);
    }

    [Fact]
    public async Task GetPropertyStatementAsync_ComputesWindowOpeningAndRunningBalanceFromLedger()
    {
        var selectedMonth = new DateTime(2025, 3, 1);
        var dataAccess = new FakePropertyDashboardDataAccess
        {
            Property = new PropertyOptionVm { Id = 8, Name = "Flat", AddressLine1 = "45 Oak", IsActive = true },
            ActiveLease = new ActiveLeaseDataModel { LeaseId = 12, TenantId = 16, TenantName = "Alice", PaymentReference = "ALICE-UNIT-8", StartDate = new DateTime(2023, 6, 1) },
            OpeningOutstanding = 1000m,
            AmountBeforeDate = 400m,
            Snapshot = new StatementSnapshotDataModel { AmountThroughMonth = 900m }
        };

        dataAccess.MonthEntriesByMonth[new DateTime(2025, 1, 1)] =
        [
            new StatementEntryDataModel { StatementEntryId = 1, EntryDate = new DateTime(2025, 1, 10), EntryType = "Rent", Description = "Rent", Amount = 100m, SourceTable = "rent_rate" }
        ];

        dataAccess.MonthEntriesByMonth[new DateTime(2025, 2, 1)] =
        [
            new StatementEntryDataModel { StatementEntryId = 2, EntryDate = new DateTime(2025, 2, 10), EntryType = "Payment", Description = "Pay", Amount = -50m, SourceTable = "payment" }
        ];

        dataAccess.MonthEntriesByMonth[new DateTime(2025, 3, 1)] =
        [
            new StatementEntryDataModel { StatementEntryId = 3, EntryDate = new DateTime(2025, 3, 10), EntryType = "Service", Description = "Water", Amount = 20m, SourceTable = "service_charge" }
        ];

        var sut = new PropertyDashboardManager(dataAccess, new FakeEmailService());

        var statement = await sut.GetPropertyStatementAsync(8, selectedMonth);

        Assert.NotNull(statement);
        Assert.Equal(1400m, statement!.OpeningOutstanding);
        Assert.Equal(1900m, statement.CurrentBalance);
        Assert.Equal("ALICE-UNIT-8", statement.PaymentReference);

        Assert.Equal(new DateTime(2025, 1, 1), dataAccess.RequestedStatementMonths[0]);
        Assert.Equal(new DateTime(2025, 2, 1), dataAccess.RequestedStatementMonths[1]);
        Assert.Equal(new DateTime(2025, 3, 1), dataAccess.RequestedStatementMonths[2]);

        Assert.Collection(
            statement.Entries,
            jan => Assert.Equal(1500m, jan.RunningBalance),
            feb => Assert.Equal(1450m, feb.RunningBalance),
            mar => Assert.Equal(1470m, mar.RunningBalance));
    }

    [Fact]
    public async Task GetPropertyStatementAsync_OnlyMarksKnownSourceRowsAsEditable()
    {
        var selectedMonth = new DateTime(2025, 3, 1);
        var dataAccess = new FakePropertyDashboardDataAccess
        {
            Property = new PropertyOptionVm { Id = 2, Name = "Flat", AddressLine1 = "Address", IsActive = true },
            ActiveLease = new ActiveLeaseDataModel { LeaseId = 3, TenantId = 4, TenantName = "Tenant", StartDate = new DateTime(2024, 1, 1) },
            Snapshot = new StatementSnapshotDataModel(),
        };

        dataAccess.MonthEntriesByMonth[new DateTime(2025, 1, 1)] =
        [
            new StatementEntryDataModel { StatementEntryId = 10, EntryDate = new DateTime(2025, 1, 5), EntryType = "Rent", Description = "Rent", Amount = 10m, SourceTable = "rent_rate" },
            new StatementEntryDataModel { StatementEntryId = 11, EntryDate = new DateTime(2025, 1, 6), EntryType = "Manual", Description = "Manual", Amount = 10m, SourceTable = "manual_adjustment" }
        ];

        var sut = new PropertyDashboardManager(dataAccess, new FakeEmailService());
        var statement = await sut.GetPropertyStatementAsync(2, selectedMonth);

        Assert.NotNull(statement);
        var rentRow = Assert.Single(statement!.Entries.Where(x => x.StatementEntryId == 10));
        var manualRow = Assert.Single(statement.Entries.Where(x => x.StatementEntryId == 11));
        Assert.True(rentRow.CanEdit);
        Assert.False(manualRow.CanEdit);
    }


    [Fact]
    public async Task GeneratePropertyStatementPdfAsync_UploadsPdfToGoogleDrive_WhenStorageServiceProvided()
    {
        var dataAccess = new FakePropertyDashboardDataAccess
        {
            Property = new PropertyOptionVm { Id = 5, Name = "Flat", AddressLine1 = "Address", IsActive = true },
            ActiveLease = new ActiveLeaseDataModel { LeaseId = 6, TenantId = 7, TenantName = "Tenant", PaymentReference = "REF", StartDate = new DateTime(2024, 1, 1) },
            Snapshot = new StatementSnapshotDataModel()
        };
        var googleDrive = new FakeGoogleDriveStorageService();
        var sut = new PropertyDashboardManager(dataAccess, new FakeEmailService(), googleDrive);

        var result = await sut.GeneratePropertyStatementPdfAsync(5, new DateTime(2025, 3, 1));

        Assert.NotNull(result);
        Assert.Equal("google-drive-file-id", result!.GoogleDriveFileId);
        Assert.Equal(1, googleDrive.UploadCount);
        Assert.Equal(result.FileName, googleDrive.LastFileName);
        Assert.Equal("application/pdf", googleDrive.LastContentType);
        Assert.NotEmpty(googleDrive.LastContent);
    }

    [Fact]
    public async Task SendTenantEmailAsync_ReturnsError_WhenTenantEmailMissing()
    {
        var dataAccess = new FakePropertyDashboardDataAccess
        {
            ActiveLease = new ActiveLeaseDataModel { LeaseId = 1, TenantId = 2, TenantName = "Tenant", TenantEmail = string.Empty, StartDate = DateTime.UtcNow }
        };
        var emailService = new FakeEmailService();
        var sut = new PropertyDashboardManager(dataAccess, emailService);

        var result = await sut.SendTenantEmailAsync(new SendTenantEmailRequestVm
        {
            PropertyId = 5
        });

        Assert.False(result.Success);
        Assert.Contains("does not have an email", result.Message);
        Assert.Equal(0, emailService.SendCount);
    }

    [Fact]
    public async Task SendTenantEmailAsync_SendsToActiveTenantEmail()
    {
        var dataAccess = new FakePropertyDashboardDataAccess
        {
            Property = new PropertyOptionVm { Id = 5, Name = "Flat", AddressLine1 = "45 Oak", IsActive = true },
            ActiveLease = new ActiveLeaseDataModel { LeaseId = 1, TenantId = 2, TenantName = "Tenant", TenantEmail = "tenant@example.com", StartDate = DateTime.UtcNow },
            Snapshot = new StatementSnapshotDataModel()
        };
        dataAccess.MonthEntriesByMonth[new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1)] =
        [
            new StatementEntryDataModel { StatementEntryId = 1, EntryDate = DateTime.UtcNow.Date, EntryType = "Rent", Description = "Rent", Amount = 100m, SourceTable = "rent_rate" }
        ];
        var emailService = new FakeEmailService();
        var sut = new PropertyDashboardManager(dataAccess, emailService);

        var result = await sut.SendTenantEmailAsync(new SendTenantEmailRequestVm
        {
            PropertyId = 5
        });

        Assert.True(result.Success);
        Assert.Equal("tenant@example.com", result.RecipientEmail);
        Assert.Equal(1, emailService.SendCount);
        Assert.Equal("tenant@example.com", emailService.LastToEmail);
        Assert.Contains("Statement -", emailService.LastSubject);
        Assert.Contains("Please see attached statement", emailService.LastBody);
        Assert.NotNull(emailService.LastAttachmentBytes);
        Assert.NotEmpty(emailService.LastAttachmentBytes!);
    }



    [Fact]
    public async Task SavePaymentsAsync_SendsLateNoticeEmail_WhenLateInterestWasAdded()
    {
        var dataAccess = new FakePropertyDashboardDataAccess
        {
            ActiveLease = new ActiveLeaseDataModel { LeaseId = 21, TenantId = 22, TenantName = "Wayne", TenantEmail = "wayne@example.com", StartDate = new DateTime(2024, 1, 1) },
            LatePaymentCharges =
            [
                new LatePaymentChargeDataModel
                {
                    LeaseId = 21,
                    TenantId = 22,
                    TenantName = "Wayne",
                    TenantEmail = "wayne@example.com",
                    PaidOn = new DateTime(2026, 6, 10),
                    BalanceBeforePayment = 10000m,
                    BalanceAfterPayment = 1000m,
                    DaysLate = 6,
                    InterestAmount = 101.78m,
                    LetterAmount = 200m,
                    CurrentBalance = 1301.78m,
                    InterestDescription = "Late payment interest: R 10,000.00 x 6/365 x 23% = R 37.81; outstanding after payment R 1,000.00 x 30/365 x 23% = R 18.90"
                }
            ]
        };
        var emailService = new FakeEmailService();
        var sut = new PropertyDashboardManager(dataAccess, emailService);

        var result = await sut.SavePaymentsAsync(new SavePaymentsRequestVm
        {
            PropertyId = 9,
            Payments = [new PaymentCandidateVm { PaidOn = new DateTime(2026, 6, 10), Amount = 9000m, Description = "Rent payment" }]
        });

        Assert.True(result.Success);
        Assert.Equal(1, emailService.SendCount);
        Assert.Equal("wayne@example.com", emailService.LastToEmail);
        Assert.Contains("Late rent - demand for payment", emailService.LastSubject);
        Assert.Contains("THIS ENTIRE BALANCE MUST BE PAID IMMEDIATELY", emailService.LastBody);
        Assert.Contains("Late payment letter: R 200.00", emailService.LastBody);
    }

    [Fact]
    public async Task SavePaymentsAsync_DoesNotApplyLateCharges_WhenPaymentIsOnOrBeforeFourth()
    {
        var dataAccess = new FakePropertyDashboardDataAccess
        {
            ActiveLease = new ActiveLeaseDataModel { LeaseId = 21, TenantId = 22, TenantName = "Wayne", TenantEmail = "wayne@example.com", StartDate = new DateTime(2024, 1, 1) },
            LatePaymentCharges =
            [
                new LatePaymentChargeDataModel
                {
                    LeaseId = 21,
                    TenantId = 22,
                    TenantName = "Wayne",
                    TenantEmail = "wayne@example.com",
                    PaidOn = new DateTime(2026, 6, 1),
                    InterestAmount = 385.95m,
                    CurrentBalance = 19154.73m,
                    InterestDescription = "Should not be used"
                }
            ]
        };
        var emailService = new FakeEmailService();
        var sut = new PropertyDashboardManager(dataAccess, emailService);

        var result = await sut.SavePaymentsAsync(new SavePaymentsRequestVm
        {
            PropertyId = 9,
            Payments = [new PaymentCandidateVm { PaidOn = new DateTime(2026, 6, 1), Amount = 9000m, Description = "Payment received, thank you" }]
        });

        Assert.True(result.Success);
        Assert.Equal(0, dataAccess.ApplyLatePaymentChargesCallCount);
        Assert.Equal(0, emailService.SendCount);
        Assert.Contains("Added late interest for 0 late payment(s)", result.Message);
    }


    [Fact]
    public async Task SaveManualLateChargeAsync_SendsLateLetterEmail_WhenLetterFeeAdded()
    {
        var dataAccess = new FakePropertyDashboardDataAccess
        {
            Property = new PropertyOptionVm { Id = 9, Name = "House", AddressLine1 = "1 Main", IsActive = true },
            ActiveLease = new ActiveLeaseDataModel { LeaseId = 21, TenantId = 22, TenantName = "Wayne", TenantEmail = "wayne@example.com", StartDate = new DateTime(2024, 1, 1) },
            OpeningOutstanding = 10000m,
            Snapshot = new StatementSnapshotDataModel { AmountThroughMonth = 200m }
        };
        var emailService = new FakeEmailService();
        var sut = new PropertyDashboardManager(dataAccess, emailService);

        var result = await sut.SaveManualLateChargeAsync(new ManualLateChargeRequestVm
        {
            PropertyId = 9,
            ChargeDate = new DateTime(2026, 6, 10),
            InterestAmount = 101.78m,
            AddLetterFee = true,
            Notes = "Manual late rent interest"
        });

        Assert.True(result.Success);
        Assert.Equal(1, emailService.SendCount);
        Assert.Equal("wayne@example.com", emailService.LastToEmail);
        Assert.Contains("Late rent - demand for payment", emailService.LastSubject);
        Assert.Contains("Late payment letter: R 200.00", emailService.LastBody);
        Assert.Contains("THIS ENTIRE BALANCE MUST BE PAID IMMEDIATELY", emailService.LastBody);
    }


    [Fact]
    public void ParsePaymentRows_ParsesAfrikaansMonthStatementDates()
    {
        const string statementText = "Staatdatum : 9 Mei 2026 Transaksies in RAND (ZAR) 02 MeiDebiet Order Krediet Investecpbsbusiso Ngcobo11,000.00Kt105,683.94Kt";
        var parseMethod = typeof(PropertyDashboardManager).GetMethod("ParsePaymentRows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(parseMethod);
        var payments = Assert.IsType<List<PaymentCandidateVm>>(parseMethod!.Invoke(null, [statementText, "Investecpbsbusiso Ngcobo"]));

        var payment = Assert.Single(payments);
        Assert.Equal(new DateTime(2026, 5, 2), payment.PaidOn);
        Assert.Equal(11000m, payment.Amount);
    }


    [Fact]
    public void ParsePaymentRows_DoesNotIncludeTrailingReferenceDigitInAmount()
    {
        const string statementText = "Tak NommerRekeningnommerDatumDDA Q7/94/SO/KM/KM/PA/P6/A6/PX/YFN855624994202312026/06/10FNB FUSION PRIVATE WEALTH ACCBladsy 1van 2Leweringswyse F1 R06NS/10/WV/DDA Q7855101493XSTZFN0:62499420231BBST142 071562 MNR HEIN R LOUW9 WATERBERG STR EXT 6NOORDHEUWEL1739Nie VerskafBank BTW 01 Jun Rtc Krediet W I Cornish - Rent 161Aa980C49,000.00Kt 80,040.54Kt";
        var parseMethod = typeof(PropertyDashboardManager).GetMethod("ParsePaymentRows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(parseMethod);
        var payments = Assert.IsType<List<PaymentCandidateVm>>(parseMethod!.Invoke(null, [statementText, "Rtc Krediet W I Cornish - Rent"]));

        var payment = Assert.Single(payments);
        Assert.Equal(new DateTime(2026, 6, 1), payment.PaidOn);
        Assert.Equal(9000m, payment.Amount);
    }


    [Fact]
    public void ParseMeterReadingByType_StillParsesOriginalMeterReadingsSection()
    {
        const string statementText = "METER READINGS ELECTRICITY 109307.000 109569.000 I 262.000 804.83 WATER 2822.000 2833.000 I 11.000 378.95 ACCOUNT DETAILS";

        var electricity = InvokeParseMeterReadingByType(statementText, "ELECTRICITY");
        var water = InvokeParseMeterReadingByType(statementText, "WATER");

        Assert.Equal(109307m, GetNullableDecimal(electricity, "OldReading"));
        Assert.Equal(109569m, GetNullableDecimal(electricity, "NewReading"));
        Assert.Equal(804.83m, GetNullableDecimal(electricity, "LeviedAmount"));
        Assert.Equal(2822m, GetNullableDecimal(water, "OldReading"));
        Assert.Equal(2833m, GetNullableDecimal(water, "NewReading"));
        Assert.Equal(378.95m, GetNullableDecimal(water, "LeviedAmount"));
    }

    [Fact]
    public void ParseMeterReadingByType_ParsesLatestCompressedInvoiceMeterLines()
    {
        const string statementText = "2026-05-01Invoice INV02597 (Line 1)Water (2026-03-01 to 2026-04-01) - Previous:2822, Current: 2833 - Usage: 11378.9505 423.21" +
            "2026-05-01Invoice INV02597 (Line 2)Electricity (2026-03-01 to 2026-04-01) - Previous:109307, Current: 109569 - Usage: 262804.8306 228.04" +
            "2026-06-01Invoice INV02669 (Line 3)Water (2026-04-01 to 2026-05-01) - Previous:2833, Current: 2845 - Usage: 12418.20010 343.09" +
            "2026-06-01Invoice INV02669 (Line 4)Electricity (2026-04-01 to 2026-05-01) - Previous:109569, Current: 109938 - Usage: 3691133.52011 476.61";

        var electricity = InvokeParseMeterReadingByType(statementText, "ELECTRICITY");
        var water = InvokeParseMeterReadingByType(statementText, "WATER");

        Assert.Equal(109569m, GetNullableDecimal(electricity, "OldReading"));
        Assert.Equal(109938m, GetNullableDecimal(electricity, "NewReading"));
        Assert.Equal(1133.52m, GetNullableDecimal(electricity, "LeviedAmount"));
        Assert.Equal(2833m, GetNullableDecimal(water, "OldReading"));
        Assert.Equal(2845m, GetNullableDecimal(water, "NewReading"));
        Assert.Equal(418.20m, GetNullableDecimal(water, "LeviedAmount"));
    }

    private static object InvokeParseMeterReadingByType(string statementText, string meterType)
    {
        var parseMethod = typeof(PropertyDashboardManager).GetMethod("ParseMeterReadingByType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(parseMethod);
        var result = parseMethod!.Invoke(null, [statementText, meterType]);

        Assert.NotNull(result);
        return result!;
    }

    private static decimal? GetNullableDecimal(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(property);
        var value = property!.GetValue(source);
        return value is null ? null : Assert.IsType<decimal>(value);
    }

    private sealed class FakePropertyDashboardDataAccess : IPropertyDashboardDataAccess
    {
        public PropertyOptionVm? Property { get; set; }
        public ActiveLeaseDataModel? ActiveLease { get; set; }
        public decimal OpeningOutstanding { get; set; }
        public decimal? LatestRent { get; set; }
        public StatementSnapshotDataModel Snapshot { get; set; } = new();
        public decimal AmountBeforeDate { get; set; }
        public Dictionary<DateTime, List<StatementEntryDataModel>> MonthEntriesByMonth { get; } = new();
        public List<DateTime> RequestedStatementMonths { get; } = new();
        public List<LatePaymentChargeDataModel> LatePaymentCharges { get; set; } = [];
        public int ApplyLatePaymentChargesCallCount { get; private set; }

        public Task<List<PropertyOptionVm>> LoadPropertiesAsync() => Task.FromResult(new List<PropertyOptionVm>());
        public Task<PropertyOptionVm?> LoadPropertyAsync(int propertyId) => Task.FromResult(Property);
        public Task<ActiveLeaseDataModel?> LoadActiveLeaseAsync(int propertyId) => Task.FromResult(ActiveLease);
        public Task<List<ActiveLeasePaymentMatchDataModel>> LoadActiveLeasesForPaymentMatchingAsync(DateTime asOfDate) => Task.FromResult(new List<ActiveLeasePaymentMatchDataModel>());
        public Task<decimal> LoadOpeningOutstandingAsync(int tenantId) => Task.FromResult(OpeningOutstanding);
        public Task<decimal?> LoadLatestRentAsync(int leaseId, DateTime asOfDate) => Task.FromResult(LatestRent);
        public Task<StatementSnapshotDataModel> LoadStatementSnapshotAsync(int leaseId, DateTime monthStart) => Task.FromResult(Snapshot);
        public Task<decimal> LoadStatementAmountBeforeDateAsync(int leaseId, DateTime beforeDateExclusive) => Task.FromResult(AmountBeforeDate);

        public Task<List<StatementEntryDataModel>> LoadMonthEntriesAsync(int leaseId, DateTime monthStart)
        {
            var normalized = new DateTime(monthStart.Year, monthStart.Month, 1);
            RequestedStatementMonths.Add(normalized);
            return Task.FromResult(MonthEntriesByMonth.TryGetValue(normalized, out var rows) ? rows : new List<StatementEntryDataModel>());
        }

        public Task<UpdateStatementEntryResultVm> UpdateStatementEntryAsync(int leaseId, long statementEntryId, DateTime entryDate, decimal amount, string description) => throw new NotImplementedException();
        public Task<int> InsertServiceChargesAsync(int leaseId, List<ServiceChargeInsertDataModel> charges) => throw new NotImplementedException();
        public Task<int> InsertPropertyTaxEntriesAsync(List<PropertyTaxEntryInsertDataModel> entries) => Task.FromResult(entries.Count);
        public Task<int> UpsertRentRateAsync(int leaseId, DateTime effectiveFrom, decimal amount, string notes) => throw new NotImplementedException();
        public Task<bool> PaymentExistsAsync(int leaseId, DateTime paidOn, decimal amount) => Task.FromResult(false);
        public Task<int> InsertPaymentsAsync(int leaseId, List<PaymentInsertDataModel> payments) => Task.FromResult(payments.Count);
        public Task<List<LatePaymentChargeDataModel>> ApplyLatePaymentChargesAsync(int leaseId, List<PaymentInsertDataModel> payments)
        {
            ApplyLatePaymentChargesCallCount++;
            return Task.FromResult(LatePaymentCharges);
        }

        public Task<int> InsertManualLateChargesAsync(int leaseId, DateTime chargeDate, decimal interestAmount, bool addLetterFee, string notes)
            => Task.FromResult((interestAmount > 0 ? 1 : 0) + (addLetterFee ? 1 : 0));
    }


    private sealed class FakeGoogleDriveStorageService : IGoogleDriveStorageService
    {
        public int UploadCount { get; private set; }
        public string LastFileName { get; private set; } = string.Empty;
        public byte[] LastContent { get; private set; } = [];
        public string LastContentType { get; private set; } = string.Empty;

        public Task<GoogleDriveConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new GoogleDriveConnectionTestResult());

        public Task<string?> UploadFileToFoldersAsync(string fileName, byte[] content, string contentType, IReadOnlyList<string> folderNames, CancellationToken cancellationToken = default)
            => UploadFileAsync(fileName, content, contentType, cancellationToken);

        public Task<string?> UploadFileAsync(string fileName, byte[] content, string contentType, CancellationToken cancellationToken = default)
        {
            UploadCount++;
            LastFileName = fileName;
            LastContent = content;
            LastContentType = contentType;
            return Task.FromResult<string?>("google-drive-file-id");
        }
    }

    private sealed class FakeEmailService : IEmailService
    {
        public int SendCount { get; private set; }
        public string LastToEmail { get; private set; } = string.Empty;
        public string LastSubject { get; private set; } = string.Empty;
        public string LastBody { get; private set; } = string.Empty;
        public byte[]? LastAttachmentBytes { get; private set; }

        public Task SendEmailAsync(string toEmail, string subject, string body, byte[]? attachmentBytes = null, string? attachmentFileName = null, string? attachmentContentType = null)
        {
            SendCount++;
            LastToEmail = toEmail;
            LastSubject = subject;
            LastBody = body;
            LastAttachmentBytes = attachmentBytes;
            return Task.CompletedTask;
        }
    }
}
