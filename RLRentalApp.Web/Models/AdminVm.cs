namespace RLRentalApp.Models;

public class AdminVm
{
    public List<PropertyOptionVm> Properties { get; set; } = [];
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CreatePropertyRequestVm
{
    public string Name { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class OnboardExistingPropertyRequestVm
{
    public string PropertyName { get; set; } = string.Empty;
    public string PropertyAddressLine1 { get; set; } = string.Empty;
    public string PropertyAddressLine2 { get; set; } = string.Empty;
    public string PropertyNotes { get; set; } = string.Empty;

    public string TenantFullName { get; set; } = string.Empty;
    public string TenantEmail { get; set; } = string.Empty;
    public string TenantPhone { get; set; } = string.Empty;
    public DateTime LeaseStartDate { get; set; }

    public decimal OpeningOutstanding { get; set; }
    public decimal DepositHeld { get; set; }
    public decimal InitialRent { get; set; }
}

public class AddTenantToPropertyRequestVm
{
    public int PropertyId { get; set; }
    public string TenantFullName { get; set; } = string.Empty;
    public string TenantEmail { get; set; } = string.Empty;
    public string TenantPhone { get; set; } = string.Empty;
    public DateTime LeaseStartDate { get; set; }
    public decimal OpeningOutstanding { get; set; }
    public decimal DepositHeld { get; set; }
    public decimal InitialRent { get; set; }
}
