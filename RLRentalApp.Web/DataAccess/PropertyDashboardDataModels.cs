namespace RLRentalApp.Web.DataAccess;

public sealed class ActiveLeaseDataModel
{
    public int LeaseId { get; set; }
    public int TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
}

public sealed class StatementEntryDataModel
{
    public DateTime EntryDate { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class ServiceChargeInsertDataModel
{
    public string ServiceTypeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime BillingPeriod { get; set; }
    public string Notes { get; set; } = string.Empty;
}


public sealed class PaymentInsertDataModel
{
    public DateTime PaidOn { get; set; }
    public decimal Amount { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}
