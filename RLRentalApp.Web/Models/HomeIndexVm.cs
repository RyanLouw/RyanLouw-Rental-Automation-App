namespace RLRentalApp.Models;

public class HomeIndexVm
{
    public List<PropertyOptionVm> Properties { get; set; } = [];
}

public class PropertyOptionVm
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class PropertyStatusVm
{
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string PropertyAddress { get; set; } = string.Empty;
    public bool IsPropertyActive { get; set; }
    public bool HasActiveLease { get; set; }
    public int? LeaseId { get; set; }
    public int? TenantId { get; set; }
    public string? TenantName { get; set; }
    public DateTime? LeaseStartDate { get; set; }
    public decimal? LatestRent { get; set; }
    public decimal OpeningOutstanding { get; set; }
    public decimal CurrentMonthServiceTotal { get; set; }
    public decimal CurrentMonthPaymentTotal { get; set; }
    public decimal CurrentBalance { get; set; }
}

public class PropertyStatementVm
{
    public int PropertyId { get; set; }
    public int LeaseId { get; set; }
    public int TenantId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public decimal OpeningOutstanding { get; set; }
    public decimal CurrentBalance { get; set; }
    public List<PropertyStatementEntryVm> Entries { get; set; } = [];
}

public class PropertyStatementEntryVm
{
    public DateTime EntryDate { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
}
