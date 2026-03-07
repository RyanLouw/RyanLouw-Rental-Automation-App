using RLRentalApp.Models;
using RLRentalApp.Web.DataAccess;
using RLRentalApp.Web.Managers;

namespace RLRentalApp.Web.Tests;

public class PropertyDashboardManagerTests
{
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

        var sut = new PropertyDashboardManager(dataAccess);

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
            ActiveLease = new ActiveLeaseDataModel { LeaseId = 12, TenantId = 16, TenantName = "Alice", StartDate = new DateTime(2023, 6, 1) },
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

        var sut = new PropertyDashboardManager(dataAccess);

        var statement = await sut.GetPropertyStatementAsync(8, selectedMonth);

        Assert.NotNull(statement);
        Assert.Equal(1400m, statement!.OpeningOutstanding);
        Assert.Equal(1900m, statement.CurrentBalance);

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

        var sut = new PropertyDashboardManager(dataAccess);
        var statement = await sut.GetPropertyStatementAsync(2, selectedMonth);

        Assert.NotNull(statement);
        var rentRow = Assert.Single(statement!.Entries.Where(x => x.StatementEntryId == 10));
        var manualRow = Assert.Single(statement.Entries.Where(x => x.StatementEntryId == 11));
        Assert.True(rentRow.CanEdit);
        Assert.False(manualRow.CanEdit);
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

        public Task<List<PropertyOptionVm>> LoadPropertiesAsync() => Task.FromResult(new List<PropertyOptionVm>());
        public Task<PropertyOptionVm?> LoadPropertyAsync(int propertyId) => Task.FromResult(Property);
        public Task<ActiveLeaseDataModel?> LoadActiveLeaseAsync(int propertyId) => Task.FromResult(ActiveLease);
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
        public Task<int> UpsertRentRateAsync(int leaseId, DateTime effectiveFrom, decimal amount, string notes) => throw new NotImplementedException();
        public Task<bool> PaymentExistsAsync(int leaseId, DateTime paidOn, decimal amount) => throw new NotImplementedException();
        public Task<int> InsertPaymentsAsync(int leaseId, List<PaymentInsertDataModel> payments) => throw new NotImplementedException();
    }
}
