namespace RLRentalApp.Web.Services;

public interface IGoogleDriveStorageService
{
    Task<GoogleDriveConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<string> UploadFileToFoldersAsync(string fileName, byte[] content, string contentType, IReadOnlyList<string> folderNames, CancellationToken cancellationToken = default);

    Task<string> UploadFileAsync(
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default);
}
