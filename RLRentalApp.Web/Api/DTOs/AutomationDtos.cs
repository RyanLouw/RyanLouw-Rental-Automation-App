using System.ComponentModel.DataAnnotations;

namespace RLRentalApp.Web.Api.DTOs;

public sealed class PropertyStatusResponseDto
{
    public int PropertyId { get; init; }
    public string PropertyName { get; init; } = string.Empty;
    public string PropertyAddress { get; init; } = string.Empty;
    public bool IsPropertyActive { get; init; }
    public bool HasActiveLease { get; init; }
    public int? LeaseId { get; init; }
    public int? TenantId { get; init; }
    public string? TenantName { get; init; }
    public string? TenantEmail { get; init; }
    public DateTime? LeaseStartDate { get; init; }
    public decimal? LatestRentAmount { get; init; }
    public DateTime? LatestRentDate { get; init; }
    public decimal OpeningOutstanding { get; init; }
    public decimal CurrentMonthServiceTotal { get; init; }
    public decimal CurrentMonthPaymentTotal { get; init; }
    public decimal CurrentBalance { get; init; }
}

public sealed class SaveRentRequestDto
{
    [Range(1, int.MaxValue)]
    public int PropertyId { get; init; }

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Amount { get; init; }

    [Required]
    public DateTime EffectiveFrom { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

public sealed class SaveServicesRequestDto
{
    [Range(1, int.MaxValue)]
    public int PropertyId { get; init; }

    [Required]
    [MinLength(1)]
    public List<ServiceChargeItemDto> Services { get; init; } = [];
}

public sealed class ServiceChargeItemDto
{
    [Required]
    [StringLength(150)]
    public string ServiceTypeName { get; init; } = string.Empty;

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Amount { get; init; }

    [Required]
    public DateTime BillingPeriod { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

public sealed class SavePaymentsRequestDto
{
    [Range(1, int.MaxValue)]
    public int PropertyId { get; init; }

    [Required]
    [MinLength(1)]
    public List<PaymentItemDto> Payments { get; init; } = [];
}

public sealed class PaymentItemDto
{
    [Required]
    public DateTime PaidOn { get; init; }

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Amount { get; init; }

    [StringLength(200)]
    public string? Reference { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

public sealed class AutomationCommandResponseDto
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public object? Data { get; init; }
}
