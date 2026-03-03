using RLRentalApp.Models;
using RLRentalApp.Web.DataAccess;

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
}
