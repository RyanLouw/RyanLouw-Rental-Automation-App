namespace RLRentalApp.Web.Services;

public class GoogleDriveOptions
{
    public const string SectionName = "GoogleDrive";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = "RLRentalApp";
    public bool Enabled { get; set; }
}
