namespace RLRentalApp.Models;

public class TaxReportVm
{
    public List<PropertyOptionVm> Properties { get; set; } = [];
    public int? PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public DateTime FromMonth { get; set; }
    public DateTime ToMonth { get; set; }
    public List<string> Months { get; set; } = [];
    public List<string> Descriptions { get; set; } = [];
    public List<TaxLedgerEntryVm> Entries { get; set; } = [];
    public bool IsLoaded => PropertyId is > 0 && Months.Count > 0;
}

public class TaxLedgerEntryVm
{
    public DateTime EntryDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class TaxReportDocumentVm
{
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string FromMonth { get; set; } = string.Empty;
    public string ToMonth { get; set; } = string.Empty;
    public List<string> Months { get; set; } = [];
    public List<TaxReportSectionVm> Sections { get; set; } = [];
}

public class TaxReportSectionVm
{
    public string Name { get; set; } = string.Empty;
    public List<TaxReportRowVm> Rows { get; set; } = [];
}

public class TaxReportRowVm
{
    public string Description { get; set; } = string.Empty;
    public List<decimal> Values { get; set; } = [];
}
