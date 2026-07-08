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
    Task<ServicePdfParseResultVm> ParseServicePdfAsync(IFormFile? file, string? password = null);
    Task<SaveServicesResultVm> SaveServicesAsync(SaveServicesRequestVm request);
    Task<SaveRentResultVm> SaveRentAsync(SaveRentRequestVm request);
    Task<PaymentPdfParseResultVm> ParsePaymentPdfAsync(IFormFile? file, string? descriptionContains);
    Task<PaymentPdfParseResultVm> ParseAllRentersPaymentPdfAsync(IFormFile? file);
    Task<SavePaymentsResultVm> SavePaymentsAsync(SavePaymentsRequestVm request);
    Task<ManualLateChargeResultVm> SaveManualLateChargeAsync(ManualLateChargeRequestVm request);
    Task<SendTenantEmailResultVm> SendTenantEmailAsync(SendTenantEmailRequestVm request);
    Task<TaxDashboardVm> GetTaxDashboardAsync(int? year = null);
    Task<TaxDocumentUploadResultVm> SaveBankStatementAsync(IFormFile? file, int year, int month);
    Task<TaxDocumentUploadResultVm> SaveExpenseDocumentAsync(IFormFile? file, int propertyId, int year, int month);
}
