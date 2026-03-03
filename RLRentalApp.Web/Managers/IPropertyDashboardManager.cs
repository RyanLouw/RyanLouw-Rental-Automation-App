using Microsoft.AspNetCore.Http;
using RLRentalApp.Models;

namespace RLRentalApp.Web.Managers;

public interface IPropertyDashboardManager
{
    Task<HomeIndexVm> GetDashboardAsync();
    Task<PropertyStatusVm?> GetPropertyStatusAsync(int propertyId);
    Task<PropertyStatementVm?> GetPropertyStatementAsync(int propertyId, DateTime? statementMonth = null);
    Task<ServicePdfParseResultVm> ParseServicePdfAsync(IFormFile? file);
    Task<SaveServicesResultVm> SaveServicesAsync(SaveServicesRequestVm request);
    Task<PaymentPdfParseResultVm> ParsePaymentPdfAsync(IFormFile? file, string? descriptionContains);
    Task<SavePaymentsResultVm> SavePaymentsAsync(SavePaymentsRequestVm request);
}
