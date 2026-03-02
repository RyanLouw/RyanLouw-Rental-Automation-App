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
    public string? TenantName { get; set; }
    public DateTime? LeaseStartDate { get; set; }
    public decimal? LatestRent { get; set; }
    public decimal CurrentMonthServiceTotal { get; set; }
    public decimal CurrentMonthPaymentTotal { get; set; }
    public decimal CurrentBalance { get; set; }
}
