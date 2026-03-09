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
