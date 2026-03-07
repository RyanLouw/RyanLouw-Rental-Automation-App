using RLRentalApp.Models;

namespace RLRentalApp.Web.DataAccess;

public interface IPropertyDashboardDataAccess
{
    Task<List<PropertyOptionVm>> LoadPropertiesAsync();
    Task<PropertyOptionVm?> LoadPropertyAsync(int propertyId);
    Task<ActiveLeaseDataModel?> LoadActiveLeaseAsync(int propertyId);
    Task<decimal> LoadOpeningOutstandingAsync(int tenantId);
    Task<decimal?> LoadLatestRentAsync(int leaseId);
    Task<decimal> LoadCurrentMonthServiceTotalAsync(int leaseId);
    Task<decimal> LoadCurrentMonthPaymentTotalAsync(int leaseId);
    Task<List<StatementEntryDataModel>> LoadMonthEntriesAsync(int leaseId, DateTime monthStart);
    Task<int> InsertServiceChargesAsync(int leaseId, List<ServiceChargeInsertDataModel> charges);
    Task<int> UpsertRentRateAsync(int leaseId, DateTime effectiveFrom, decimal amount, string notes);
    Task<bool> PaymentExistsAsync(int leaseId, DateTime paidOn, decimal amount);
    Task<int> InsertPaymentsAsync(int leaseId, List<PaymentInsertDataModel> payments);
}
