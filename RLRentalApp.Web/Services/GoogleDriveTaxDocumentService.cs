using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Text.Json;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace RLRentalApp.Web.Services;

public class GoogleDriveTaxDocumentService : IGoogleDriveTaxDocumentService
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private readonly GoogleDriveOptions _options;

    public GoogleDriveTaxDocumentService(IOptions<GoogleDriveOptions> options)
    {
        _options = options.Value;
    }

    public async Task<GoogleDriveUploadResult> UploadAsync(IFormFile file, IReadOnlyList<string> folderPathParts, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceAccountKeyPath))
        {
            throw new InvalidOperationException("GoogleDrive:ServiceAccountKeyPath is required for Drive API uploads.");
        }

        if (!System.IO.File.Exists(_options.ServiceAccountKeyPath))
        {
            throw new InvalidOperationException($"Google Drive service account key was not found at '{_options.ServiceAccountKeyPath}'.");
        }

        var driveService = CreateDriveService();
        var parentId = string.IsNullOrWhiteSpace(_options.RootFolderId) ? "root" : _options.RootFolderId;

        foreach (var folderName in folderPathParts.Select(SanitizeDriveName).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            parentId = await GetOrCreateFolderAsync(driveService, parentId, folderName, cancellationToken);
        }

        var safeFileName = SanitizeDriveName(Path.GetFileName(file.FileName));
        var uploadedFile = await UploadFileAsync(driveService, parentId, safeFileName, file, cancellationToken);

        return new GoogleDriveUploadResult
        {
            FileId = uploadedFile.Id,
            FileName = uploadedFile.Name,
            WebViewLink = uploadedFile.WebViewLink,
            FolderPath = string.Join(" / ", folderPathParts)
        };
    }




    public string GetConfiguredServiceAccountEmail()
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceAccountKeyPath) || !System.IO.File.Exists(_options.ServiceAccountKeyPath))
        {
            return string.Empty;
        }

        using var keyDocument = JsonDocument.Parse(System.IO.File.ReadAllText(_options.ServiceAccountKeyPath));
        return keyDocument.RootElement.TryGetProperty("client_email", out var clientEmail)
            ? clientEmail.GetString() ?? string.Empty
            : string.Empty;
    }

    public async Task<GoogleDriveFolderTestResult> TestFolderAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceAccountKeyPath))
        {
            throw new InvalidOperationException("GoogleDrive:ServiceAccountKeyPath is required for Drive API connection testing.");
        }

        if (!System.IO.File.Exists(_options.ServiceAccountKeyPath))
        {
            throw new FileNotFoundException("The Google service-account key was not found.", _options.ServiceAccountKeyPath);
        }

        if (string.IsNullOrWhiteSpace(_options.RootFolderId))
        {
            throw new InvalidOperationException("GoogleDrive:RootFolderId is required for Drive API connection testing.");
        }

        var driveService = CreateDriveService();
        var request = driveService.Files.Get(_options.RootFolderId);
        request.Fields = "id,name,mimeType,parents,driveId,capabilities";
        request.SupportsAllDrives = true;

        var folder = await request.ExecuteAsync(cancellationToken);
        const string expectedFolderMimeType = "application/vnd.google-apps.folder";
        if (!string.Equals(folder.MimeType, expectedFolderMimeType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The configured ID points to '{folder.Name}', but it is not a Google Drive folder.");
        }

        if (string.IsNullOrWhiteSpace(folder.DriveId))
        {
            throw new InvalidOperationException("The configured RootFolderId is in a personal My Drive folder. Service accounts do not have Google Drive storage quota, so the tax proof folder must be inside a Google Shared Drive, or the app must be changed to use user OAuth instead of a service account.");
        }

        return new GoogleDriveFolderTestResult
        {
            FolderId = folder.Id,
            FolderName = folder.Name,
            MimeType = folder.MimeType,
            SharedDriveId = folder.DriveId ?? "Not a Shared Drive"
        };
    }

    public async Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ServiceAccountKeyPath))
        {
            throw new InvalidOperationException("GoogleDrive:ServiceAccountKeyPath is required for Drive API deletes.");
        }

        if (!System.IO.File.Exists(_options.ServiceAccountKeyPath))
        {
            throw new InvalidOperationException($"Google Drive service account key was not found at '{_options.ServiceAccountKeyPath}'.");
        }

        var driveService = CreateDriveService();
        var deleteRequest = driveService.Files.Delete(fileId);
        deleteRequest.SupportsAllDrives = true;
        await deleteRequest.ExecuteAsync(cancellationToken);
    }

    private DriveService CreateDriveService()
    {
        var credential = GoogleCredential
            .FromFile(_options.ServiceAccountKeyPath)
            .CreateScoped(DriveService.Scope.Drive);

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = string.IsNullOrWhiteSpace(_options.ApplicationName) ? "RLRentalApp" : _options.ApplicationName
        });
    }

    private static async Task<string> GetOrCreateFolderAsync(DriveService driveService, string parentId, string folderName, CancellationToken cancellationToken)
    {
        var listRequest = driveService.Files.List();
        listRequest.SupportsAllDrives = true;
        listRequest.IncludeItemsFromAllDrives = true;
        listRequest.Q = $"mimeType = '{FolderMimeType}' and name = '{EscapeDriveQueryValue(folderName)}' and '{EscapeDriveQueryValue(parentId)}' in parents and trashed = false";
        listRequest.Fields = "files(id, name)";
        listRequest.PageSize = 1;

        var existingFolders = await listRequest.ExecuteAsync(cancellationToken);
        var existingFolder = existingFolders.Files.FirstOrDefault();
        if (existingFolder is not null)
        {
            return existingFolder.Id;
        }

        var folderMetadata = new DriveFile
        {
            Name = folderName,
            MimeType = FolderMimeType,
            Parents = [parentId]
        };

        var createRequest = driveService.Files.Create(folderMetadata);
        createRequest.SupportsAllDrives = true;
        createRequest.Fields = "id";
        var createdFolder = await createRequest.ExecuteAsync(cancellationToken);
        return createdFolder.Id;
    }

    private static async Task<DriveFile> UploadFileAsync(DriveService driveService, string parentId, string fileName, IFormFile file, CancellationToken cancellationToken)
    {
        var fileMetadata = new DriveFile
        {
            Name = fileName,
            Parents = [parentId]
        };

        await using var stream = file.OpenReadStream();
        var uploadRequest = driveService.Files.Create(fileMetadata, stream, file.ContentType ?? "application/octet-stream");
        uploadRequest.SupportsAllDrives = true;
        uploadRequest.Fields = "id, name, webViewLink";

        var uploadProgress = await uploadRequest.UploadAsync(cancellationToken);
        if (uploadProgress.Status != UploadStatus.Completed)
        {
            if (uploadProgress.Exception is GoogleApiException googleApiException &&
                googleApiException.HttpStatusCode == System.Net.HttpStatusCode.Forbidden &&
                googleApiException.Message.Contains("Service Accounts do not have storage quota", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Google Drive upload failed because service accounts do not have storage quota in personal My Drive folders. Move/create the tax proof root folder inside a Google Shared Drive and set GoogleDrive:RootFolderId to that Shared Drive folder ID, or switch this app to user OAuth.", googleApiException);
            }

            throw new InvalidOperationException($"Google Drive upload did not complete. Status: {uploadProgress.Status}. {uploadProgress.Exception?.Message ?? string.Empty}", uploadProgress.Exception);
        }

        return uploadRequest.ResponseBody
            ?? throw new InvalidOperationException("Google Drive upload completed but did not return uploaded file details.");
    }

    private static string SanitizeDriveName(string value)
    {
        var sanitized = value.Replace('/', '-').Replace('\\', '-').Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized;
    }

    private static string EscapeDriveQueryValue(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
