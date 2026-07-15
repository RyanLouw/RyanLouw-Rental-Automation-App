namespace RLRentalApp.Web.Services;

public interface IGoogleDriveStorageService
{
    Task<string?> UploadFileAsync(
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default);
}
