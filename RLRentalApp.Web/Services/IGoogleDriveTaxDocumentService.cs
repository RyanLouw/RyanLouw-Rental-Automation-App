using Microsoft.AspNetCore.Http;

namespace RLRentalApp.Web.Services;

public interface IGoogleDriveTaxDocumentService
{
    Task<GoogleDriveUploadResult> UploadAsync(IFormFile file, IReadOnlyList<string> folderPathParts, CancellationToken cancellationToken = default);
    string GetConfiguredServiceAccountEmail();
    Task<GoogleDriveFolderTestResult> TestFolderAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(string fileId, CancellationToken cancellationToken = default);
}

public sealed class GoogleDriveUploadResult
{
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string WebViewLink { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
}


public sealed class GoogleDriveFolderTestResult
{
    public string FolderId { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string SharedDriveId { get; set; } = string.Empty;
}
