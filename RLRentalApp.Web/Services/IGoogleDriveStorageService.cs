namespace RLRentalApp.Web.Services;

public interface IGoogleDriveStorageService
{
    Task<GoogleDriveConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<string?> UploadFileAsync(
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default);
}
