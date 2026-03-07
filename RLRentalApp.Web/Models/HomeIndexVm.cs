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
    public string AddressLine2 { get; set; } = string.Empty;
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
    public string PropertyAddress { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public decimal OpeningOutstanding { get; set; }
    public decimal CurrentBalance { get; set; }
    public DateTime StatementMonth { get; set; }
    public List<PropertyStatementEntryVm> Entries { get; set; } = [];
}

public class PropertyStatementEntryVm
{
    public long StatementEntryId { get; set; }
    public DateTime EntryDate { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
    public bool CanEdit { get; set; }
}


public class PropertyStatementPdfVm
{
    public byte[] PdfBytes { get; set; } = [];
    public string FileName { get; set; } = string.Empty;
}

public class ServicePdfParseResultVm
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public ElectricityParseVm Electricity { get; set; } = new();
    public WaterParseVm Water { get; set; } = new();
    public SewerageParseVm Sewerage { get; set; } = new();
    public RefuseParseVm Refuse { get; set; } = new();
    public string RawTextPreview { get; set; } = string.Empty;
}

public class ElectricityParseVm
{
    public decimal? OldReading { get; set; }
    public decimal? NewReading { get; set; }
    public decimal? LeviedAmount { get; set; }
}


public class WaterParseVm
{
    public decimal? OldReading { get; set; }
    public decimal? NewReading { get; set; }
    public decimal? LeviedAmount { get; set; }
}

public class SewerageParseVm
{
    public string Date { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal? AmountInclVat { get; set; }
}

public class RefuseParseVm
{
    public string Date { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal? AmountInclVat { get; set; }
}


public class SaveServicesRequestVm
{
    public int PropertyId { get; set; }
    public DateTime BillingPeriod { get; set; }
    public decimal? ElectricityAmount { get; set; }
    public decimal? WaterAmount { get; set; }
    public decimal? SewerageAmount { get; set; }
    public decimal? RefuseAmount { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class SaveServicesResultVm
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int AddedCount { get; set; }
}

public class PaymentPdfParseResultVm
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<PaymentCandidateVm> Payments { get; set; } = [];
    public string RawTextPreview { get; set; } = string.Empty;
}

public class PaymentCandidateVm
{
    public DateTime PaidOn { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class SavePaymentsRequestVm
{
    public int PropertyId { get; set; }
    public List<PaymentCandidateVm> Payments { get; set; } = [];
    public string Notes { get; set; } = string.Empty;
}

public class SavePaymentsResultVm
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int AddedCount { get; set; }
    public int SkippedDuplicates { get; set; }
    public List<PaymentCandidateVm> SavedPayments { get; set; } = [];
}


public class SaveRentRequestVm
{
    public int PropertyId { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class SaveRentResultVm
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}


public class UpdateStatementEntryRequestVm
{
    public int PropertyId { get; set; }
    public long StatementEntryId { get; set; }
    public DateTime EntryDate { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class UpdateStatementEntryResultVm
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
