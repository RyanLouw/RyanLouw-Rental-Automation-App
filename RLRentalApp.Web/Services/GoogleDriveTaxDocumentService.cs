using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
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
        await driveService.Files.Delete(fileId).ExecuteAsync(cancellationToken);
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
        uploadRequest.Fields = "id, name, webViewLink";

        await uploadRequest.UploadAsync(cancellationToken);
        return uploadRequest.ResponseBody;
    }

    private static string SanitizeDriveName(string value)
    {
        var sanitized = value.Replace('/', '-').Replace('\\', '-').Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized;
    }

    private static string EscapeDriveQueryValue(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
