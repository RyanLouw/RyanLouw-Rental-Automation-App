using Microsoft.AspNetCore.Http;
using RLRentalApp.Models;
using RLRentalApp.Web.Api.DTOs;
using RLRentalApp.Web.Managers;

namespace RLRentalApp.Web.Api.Services;

public sealed class AutomationService : IAutomationService
{
    private static readonly HashSet<string> SupportedServiceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "electricity",
        "water",
        "sewerage",
        "refuse"
    };

    private readonly IPropertyDashboardManager _propertyDashboardManager;

    public AutomationService(IPropertyDashboardManager propertyDashboardManager)
    {
        _propertyDashboardManager = propertyDashboardManager;
    }

    public async Task<PropertyStatusResponseDto?> GetPropertyStatusAsync(int propertyId)
    {
        var status = await _propertyDashboardManager.GetPropertyStatusAsync(propertyId);
        if (status is null)
        {
            return null;
        }

        return new PropertyStatusResponseDto
        {
            PropertyId = status.PropertyId,
            PropertyName = status.PropertyName,
            PropertyAddress = status.PropertyAddress,
            IsPropertyActive = status.IsPropertyActive,
            HasActiveLease = status.HasActiveLease,
            LeaseId = status.LeaseId,
            TenantId = status.TenantId,
            TenantName = status.TenantName,
            TenantEmail = status.TenantEmail,
            LeaseStartDate = status.LeaseStartDate,
            LatestRentAmount = status.LatestRent,
            OpeningOutstanding = status.OpeningOutstanding,
            CurrentMonthServiceTotal = status.CurrentMonthServiceTotal,
            CurrentMonthPaymentTotal = status.CurrentMonthPaymentTotal,
            CurrentBalance = status.CurrentBalance
        };
    }


    public async Task<ServicePdfParseResponseDto> ParseServicePdfAsync(IFormFile? file)
    {
        var result = await _propertyDashboardManager.ParseServicePdfAsync(file);

        return new ServicePdfParseResponseDto
        {
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            RawTextPreview = result.RawTextPreview,
            ElectricityOldReading = result.Electricity.OldReading,
            ElectricityNewReading = result.Electricity.NewReading,
            ElectricityLeviedAmount = result.Electricity.LeviedAmount,
            WaterOldReading = result.Water.OldReading,
            WaterNewReading = result.Water.NewReading,
            WaterLeviedAmount = result.Water.LeviedAmount,
            SewerageAmountInclVat = result.Sewerage.AmountInclVat,
            RefuseAmountInclVat = result.Refuse.AmountInclVat
        };
    }

    public async Task<PaymentPdfParseResponseDto> ParsePaymentPdfAsync(IFormFile? file, ParsePaymentPdfRequestDto request)
    {
        var result = await _propertyDashboardManager.ParsePaymentPdfAsync(file, request.DescriptionContains);

        return new PaymentPdfParseResponseDto
        {
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            RawTextPreview = result.RawTextPreview,
            Payments = result.Payments.Select(x => new PaymentCandidateResponseDto
            {
                PaidOn = x.PaidOn,
                Amount = x.Amount,
                Description = x.Description
            }).ToList()
        };
    }


    public async Task<ProcessPdfAndSaveResponseDto> ProcessPdfAndSaveAsync(IFormFile? file, ProcessPdfAndSaveRequestDto request)
    {
        if (file is null || file.Length == 0)
        {
            return new ProcessPdfAndSaveResponseDto
            {
                Success = false,
                Stage = "validation",
                DocumentType = request.DocumentType.ToString(),
                Message = "A non-empty PDF file is required.",
                Errors = ["pdfFile is required."]
            };
        }

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessPdfAndSaveResponseDto
            {
                Success = false,
                Stage = "validation",
                DocumentType = request.DocumentType.ToString(),
                Message = "Invalid file type. Only PDF files are supported.",
                Errors = ["Uploaded file extension must be .pdf."]
            };
        }

        if (request.DocumentType == AutomationPdfDocumentTypeDto.Services)
        {
            if (!request.BillingPeriod.HasValue)
            {
                return new ProcessPdfAndSaveResponseDto
                {
                    Success = false,
                    Stage = "validation",
                    DocumentType = request.DocumentType.ToString(),
                    Message = "BillingPeriod is required for service PDFs.",
                    Errors = ["billingPeriod is required when documentType is Services."]
                };
            }

            var parsed = await ParseServicePdfAsync(file);
            if (!parsed.Success)
            {
                return new ProcessPdfAndSaveResponseDto
                {
                    Success = false,
                    Stage = "parse",
                    DocumentType = request.DocumentType.ToString(),
                    Message = parsed.ErrorMessage ?? "Failed to parse services PDF.",
                    ParsedData = parsed,
                    Errors = [parsed.ErrorMessage ?? "Parse failed."]
                };
            }

            var serviceItems = new List<ServiceChargeItemDto>();
            if (parsed.ElectricityLeviedAmount is > 0)
            {
                serviceItems.Add(new ServiceChargeItemDto { ServiceTypeName = "electricity", Amount = parsed.ElectricityLeviedAmount.Value, BillingPeriod = request.BillingPeriod.Value, Notes = request.Notes });
            }

            if (parsed.WaterLeviedAmount is > 0)
            {
                serviceItems.Add(new ServiceChargeItemDto { ServiceTypeName = "water", Amount = parsed.WaterLeviedAmount.Value, BillingPeriod = request.BillingPeriod.Value, Notes = request.Notes });
            }

            if (parsed.SewerageAmountInclVat is > 0)
            {
                serviceItems.Add(new ServiceChargeItemDto { ServiceTypeName = "sewerage", Amount = parsed.SewerageAmountInclVat.Value, BillingPeriod = request.BillingPeriod.Value, Notes = request.Notes });
            }

            if (parsed.RefuseAmountInclVat is > 0)
            {
                serviceItems.Add(new ServiceChargeItemDto { ServiceTypeName = "refuse", Amount = parsed.RefuseAmountInclVat.Value, BillingPeriod = request.BillingPeriod.Value, Notes = request.Notes });
            }

            if (serviceItems.Count == 0)
            {
                return new ProcessPdfAndSaveResponseDto
                {
                    Success = false,
                    Stage = "validation",
                    DocumentType = request.DocumentType.ToString(),
                    Message = "No positive service amounts were found in the PDF to save.",
                    ParsedData = parsed,
                    Errors = ["Parsed service amounts are empty or zero."]
                };
            }

            var saveResult = await SaveServicesAsync(new SaveServicesRequestDto
            {
                PropertyId = request.PropertyId,
                Services = serviceItems
            });

            if (!saveResult.Success)
            {
                return new ProcessPdfAndSaveResponseDto
                {
                    Success = false,
                    Stage = "save",
                    DocumentType = request.DocumentType.ToString(),
                    Message = saveResult.Message,
                    ParsedData = parsed,
                    SavedData = saveResult.Data,
                    Errors = [saveResult.Message]
                };
            }

            return new ProcessPdfAndSaveResponseDto
            {
                Success = true,
                Stage = "completed",
                DocumentType = request.DocumentType.ToString(),
                Message = "Service PDF parsed and saved successfully.",
                ParsedData = parsed,
                SavedData = saveResult.Data
            };
        }

        var parsedPayment = await ParsePaymentPdfAsync(file, new ParsePaymentPdfRequestDto
        {
            DescriptionContains = request.DescriptionContains
        });

        if (!parsedPayment.Success)
        {
            return new ProcessPdfAndSaveResponseDto
            {
                Success = false,
                Stage = "parse",
                DocumentType = request.DocumentType.ToString(),
                Message = parsedPayment.ErrorMessage ?? "Failed to parse payments PDF.",
                ParsedData = parsedPayment,
                Errors = [parsedPayment.ErrorMessage ?? "Parse failed."]
            };
        }

        if (parsedPayment.Payments.Count == 0)
        {
            return new ProcessPdfAndSaveResponseDto
            {
                Success = false,
                Stage = "validation",
                DocumentType = request.DocumentType.ToString(),
                Message = "No payment rows were found in the PDF to save.",
                ParsedData = parsedPayment,
                Errors = ["Parsed payments list is empty."]
            };
        }

        var savePayments = await SavePaymentsAsync(new SavePaymentsRequestDto
        {
            PropertyId = request.PropertyId,
            Payments = parsedPayment.Payments.Select(x => new PaymentItemDto
            {
                PaidOn = x.PaidOn,
                Amount = x.Amount,
                Reference = x.Description,
                Notes = request.Notes
            }).ToList()
        });

        if (!savePayments.Success)
        {
            return new ProcessPdfAndSaveResponseDto
            {
                Success = false,
                Stage = "save",
                DocumentType = request.DocumentType.ToString(),
                Message = savePayments.Message,
                ParsedData = parsedPayment,
                SavedData = savePayments.Data,
                Errors = [savePayments.Message]
            };
        }

        return new ProcessPdfAndSaveResponseDto
        {
            Success = true,
            Stage = "completed",
            DocumentType = request.DocumentType.ToString(),
            Message = "Payment PDF parsed and saved successfully.",
            ParsedData = parsedPayment,
            SavedData = savePayments.Data
        };
    }

    public async Task<AutomationCommandResponseDto> SaveRentAsync(SaveRentRequestDto request)
    {
        var result = await _propertyDashboardManager.SaveRentAsync(new SaveRentRequestVm
        {
            PropertyId = request.PropertyId,
            EffectiveFrom = request.EffectiveFrom,
            Amount = request.Amount,
            Notes = request.Notes?.Trim() ?? string.Empty
        });

        return new AutomationCommandResponseDto
        {
            Success = result.Success,
            Message = result.Message
        };
    }

    public async Task<AutomationCommandResponseDto> SaveServicesAsync(SaveServicesRequestDto request)
    {
        var normalizedTypes = request.Services
            .Select(x => x.ServiceTypeName.Trim())
            .ToList();

        var unsupportedTypes = normalizedTypes
            .Where(type => !SupportedServiceTypes.Contains(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unsupportedTypes.Count > 0)
        {
            return new AutomationCommandResponseDto
            {
                Success = false,
                Message = $"Unsupported service type(s): {string.Join(", ", unsupportedTypes)}"
            };
        }

        if (request.Services.Select(x => x.BillingPeriod.Date).Distinct().Count() > 1)
        {
            return new AutomationCommandResponseDto
            {
                Success = false,
                Message = "All service items in one request must use the same billing period."
            };
        }

        var vm = new SaveServicesRequestVm
        {
            PropertyId = request.PropertyId,
            BillingPeriod = request.Services[0].BillingPeriod,
            ElectricityAmount = request.Services.Where(x => x.ServiceTypeName.Equals("electricity", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount),
            WaterAmount = request.Services.Where(x => x.ServiceTypeName.Equals("water", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount),
            SewerageAmount = request.Services.Where(x => x.ServiceTypeName.Equals("sewerage", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount),
            RefuseAmount = request.Services.Where(x => x.ServiceTypeName.Equals("refuse", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount),
            Notes = string.Join(" | ", request.Services.Where(x => !string.IsNullOrWhiteSpace(x.Notes)).Select(x => x.Notes!.Trim()))
        };

        var result = await _propertyDashboardManager.SaveServicesAsync(vm);

        return new AutomationCommandResponseDto
        {
            Success = result.Success,
            Message = result.Message,
            Data = new { result.AddedCount }
        };
    }

    public async Task<AutomationCommandResponseDto> SavePaymentsAsync(SavePaymentsRequestDto request)
    {
        var result = await _propertyDashboardManager.SavePaymentsAsync(new SavePaymentsRequestVm
        {
            PropertyId = request.PropertyId,
            Payments = request.Payments.Select(x => new PaymentCandidateVm
            {
                PaidOn = x.PaidOn,
                Amount = x.Amount,
                Description = x.Reference?.Trim() ?? string.Empty
            }).ToList(),
            Notes = string.Join(" | ", request.Payments.Where(x => !string.IsNullOrWhiteSpace(x.Notes)).Select(x => x.Notes!.Trim()))
        });

        return new AutomationCommandResponseDto
        {
            Success = result.Success,
            Message = result.Message,
            Data = new
            {
                result.AddedCount,
                result.SkippedDuplicates,
                SavedPayments = result.SavedPayments.Select(x => new
                {
                    x.PaidOn,
                    x.Amount,
                    x.Description
                })
            }
        };
    }
}
