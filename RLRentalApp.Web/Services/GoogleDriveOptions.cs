namespace RLRentalApp.Web.Services;

public class GoogleDriveOptions
{
    public const string SectionName = "GoogleDrive";

    public string ServiceAccountKeyPath { get; set; } = string.Empty;
    public string RootFolderId { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = "RLRentalApp";
}
