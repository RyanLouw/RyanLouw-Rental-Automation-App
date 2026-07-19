using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using GoogleFile = Google.Apis.Drive.v3.Data.File;
using Microsoft.Extensions.Options;

namespace RLRentalApp.Web.Services;

public class GoogleDriveStorageService : IGoogleDriveStorageService
{
    private readonly GoogleDriveOptions _options;

    public GoogleDriveStorageService(IOptions<GoogleDriveOptions> options)
    {
        _options = options.Value;
    }

    public async Task<GoogleDriveConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        ValidateOptions();

        using var driveService = CreateDriveService();
        var suffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var testFolderName = $"RLRentalApp-GoogleDrive-Test-{suffix}";
        var folderRequest = driveService.Files.Create(new GoogleFile
        {
            Name = testFolderName,
            MimeType = "application/vnd.google-apps.folder",
            Parents = [_options.FolderId]
        });
        folderRequest.Fields = "id,name";

        var createdFolder = await folderRequest.ExecuteAsync(cancellationToken);
        var testFileName = "connection-test.txt";
        var testContent = System.Text.Encoding.UTF8.GetBytes(
            $"Google Drive connection succeeded at {DateTime.UtcNow:O}. You can delete this test folder and file after checking them.");

        await using var testFileStream = new MemoryStream(testContent);
        var fileRequest = driveService.Files.Create(new GoogleFile
        {
            Name = testFileName,
            Parents = [createdFolder.Id]
        }, testFileStream, "text/plain");
        fileRequest.Fields = "id,name";

        var uploadResult = await fileRequest.UploadAsync(cancellationToken);
        if (uploadResult.Exception is not null)
        {
            throw uploadResult.Exception;
        }

        return new GoogleDriveConnectionTestResult
        {
            TestFolderName = testFolderName,
            TestFolderId = createdFolder.Id ?? string.Empty,
            TestFileName = testFileName,
            TestFileId = fileRequest.ResponseBody?.Id ?? string.Empty
        };
    }

    public async Task<string?> UploadFileAsync(
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        ValidateOptions();

        using var driveService = CreateDriveService();

        var fileMetadata = new GoogleFile
        {
            Name = fileName,
            Parents = [_options.FolderId]
        };

        await using var uploadStream = new MemoryStream(content);
        var request = driveService.Files.Create(fileMetadata, uploadStream, contentType);
        request.Fields = "id";

        var result = await request.UploadAsync(cancellationToken);
        if (result.Exception is not null)
        {
            throw result.Exception;
        }

        return request.ResponseBody?.Id;
    }

    private DriveService CreateDriveService()
    {
        using var credentialStream = File.OpenRead(_options.ServiceAccountJsonPath);
        var credential = GoogleCredential
            .FromStream(credentialStream)
            .CreateScoped(DriveService.Scope.DriveFile);

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = _options.ApplicationName
        });
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceAccountJsonPath))
        {
            throw new InvalidOperationException("GoogleDrive:ServiceAccountJsonPath must be configured when Google Drive uploads are enabled.");
        }

        if (!File.Exists(_options.ServiceAccountJsonPath))
        {
            throw new FileNotFoundException("Google Drive service account JSON file was not found.", _options.ServiceAccountJsonPath);
        }

        if (string.IsNullOrWhiteSpace(_options.FolderId))
        {
            throw new InvalidOperationException("GoogleDrive:FolderId must be configured when Google Drive uploads are enabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApplicationName))
        {
            throw new InvalidOperationException("GoogleDrive:ApplicationName must be configured when Google Drive uploads are enabled.");
        }
    }
}
