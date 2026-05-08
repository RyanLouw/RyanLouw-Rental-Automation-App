namespace RLRentalApp.Models;

public class AdminVm
{
    public List<PropertyOptionVm> Properties { get; set; } = [];
    public List<LeaseOptionVm> Leases { get; set; } = [];
    public List<TenantOptionVm> Tenants { get; set; } = [];
    public string ActiveTab { get; set; } = "property";
    public AdminStatementFilterVm StatementFilter { get; set; } = new();
    public AdminStatementVm? Statement { get; set; }
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
    public string PaymentReference { get; set; } = string.Empty;
    public DateTime LeaseStartDate { get; set; }

    public decimal OpeningOutstanding { get; set; }
    public decimal DepositRequired { get; set; }
    public decimal InitialRent { get; set; }
}

public class AddTenantToPropertyRequestVm
{
    public int PropertyId { get; set; }
    public string TenantFullName { get; set; } = string.Empty;
    public string TenantEmail { get; set; } = string.Empty;
    public string TenantPhone { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
    public DateTime LeaseStartDate { get; set; }
    public decimal OpeningOutstanding { get; set; }
    public decimal DepositRequired { get; set; }
    public decimal InitialRent { get; set; }
}

public class UpdateRentAdminRequestVm
{
    public int LeaseId { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class EndLeaseAdminRequestVm
{
    public int LeaseId { get; set; }
    public DateTime EndDate { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class AddManualStatementEntryRequestVm
{
    public int LeaseId { get; set; }
    public DateTime EntryDate { get; set; }
    public string EntryType { get; set; } = "Manual";
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class UpdateAdminStatementEntryRequestVm
{
    public long StatementEntryId { get; set; }
    public DateTime EntryDate { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class AdminStatementFilterVm
{
    public int? PropertyId { get; set; }
    public int? TenantId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class LeaseOptionVm
{
    public int LeaseId { get; set; }
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public int TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive => EndDate is null;
    public string DisplayName => $"{PropertyName} - {TenantName} ({StartDate:yyyy-MM-dd} to {(EndDate?.ToString("yyyy-MM-dd") ?? "current")})";
}

public class TenantOptionVm
{
    public int TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
}

public class AdminStatementVm
{
    public string ScopeTitle { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public List<AdminStatementEntryVm> Entries { get; set; } = [];
}

public class AdminStatementEntryVm
{
    public long StatementEntryId { get; set; }
    public int LeaseId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
    public bool IsCurrentTenant { get; set; }
    public bool CanEdit { get; set; }
}
