using Microsoft.AspNetCore.Http;
using RLRentalApp.Models;

namespace RLRentalApp.Web.Managers;

public interface IPropertyDashboardManager
{
    Task<HomeIndexVm> GetDashboardAsync();
    Task<PropertyStatusVm?> GetPropertyStatusAsync(int propertyId);
    Task<PropertyStatementVm?> GetPropertyStatementAsync(int propertyId, DateTime? statementMonth = null);
    Task<PropertyStatementPdfVm?> GeneratePropertyStatementPdfAsync(int propertyId, DateTime? statementMonth = null);
    Task<UpdateStatementEntryResultVm> UpdateStatementEntryAsync(UpdateStatementEntryRequestVm request);
    Task<ServicePdfParseResultVm> ParseServicePdfAsync(IFormFile? file);
    Task<SaveServicesResultVm> SaveServicesAsync(SaveServicesRequestVm request);
    Task<SaveRentResultVm> SaveRentAsync(SaveRentRequestVm request);
    Task<PaymentPdfParseResultVm> ParsePaymentPdfAsync(IFormFile? file, string? descriptionContains);
    Task<SavePaymentsResultVm> SavePaymentsAsync(SavePaymentsRequestVm request);
    Task<SendTenantEmailResultVm> SendTenantEmailAsync(SendTenantEmailRequestVm request);
}
