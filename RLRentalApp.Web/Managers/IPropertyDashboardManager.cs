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
    Task<TaxTransactionsVm> GetTaxTransactionsAsync(int? year = null);
    Task<SaveTaxTransactionResultVm> SaveTaxTransactionAsync(IFormFile? proofFile, int propertyId, DateTime transactionDate, string entryKind, decimal amount, string description);
    Task<DeleteTaxTransactionResultVm> DeleteTaxTransactionAsync(long taxTransactionId, bool deleteProofFile);
}
