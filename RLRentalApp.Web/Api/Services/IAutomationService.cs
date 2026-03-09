using Microsoft.AspNetCore.Http;
using RLRentalApp.Web.Api.DTOs;

namespace RLRentalApp.Web.Api.Services;

public interface IAutomationService
{
    Task<PropertyStatusResponseDto?> GetPropertyStatusAsync(int propertyId);
    Task<ServicePdfParseResponseDto> ParseServicePdfAsync(IFormFile? file);
    Task<PaymentPdfParseResponseDto> ParsePaymentPdfAsync(IFormFile? file, ParsePaymentPdfRequestDto request);
    Task<AutomationCommandResponseDto> SaveRentAsync(SaveRentRequestDto request);
    Task<AutomationCommandResponseDto> SaveServicesAsync(SaveServicesRequestDto request);
    Task<AutomationCommandResponseDto> SavePaymentsAsync(SavePaymentsRequestDto request);
}
