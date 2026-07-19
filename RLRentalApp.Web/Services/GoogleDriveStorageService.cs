using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using GoogleFile = Google.Apis.Drive.v3.Data.File;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace RLRentalApp.Web.Services;

public class GoogleDriveStorageService : IGoogleDriveStorageService
{
    private readonly GoogleDriveOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<IdentityUser> _userManager;

    public GoogleDriveStorageService(IOptions<GoogleDriveOptions> options, IHttpContextAccessor httpContextAccessor, UserManager<IdentityUser> userManager)
    {
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public async Task<GoogleDriveConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var driveService = await CreateDriveServiceAsync(cancellationToken);
        var suffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var testFolderName = $"RLRentalApp-GoogleDrive-Test-{suffix}";
        var folder = await driveService.Files.Create(new GoogleFile { Name = testFolderName, MimeType = "application/vnd.google-apps.folder", Parents = [_options.FolderId] }).ExecuteAsync(cancellationToken);
        var testFileName = "connection-test.txt";
        await using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"Google Drive connection succeeded at {DateTime.UtcNow:O}."));
        var upload = driveService.Files.Create(new GoogleFile { Name = testFileName, Parents = [folder.Id] }, content, "text/plain");
        var uploadResult = await upload.UploadAsync(cancellationToken);
        if (uploadResult.Exception is not null) throw uploadResult.Exception;
        return new GoogleDriveConnectionTestResult { TestFolderName = testFolderName, TestFolderId = folder.Id ?? string.Empty, TestFileName = testFileName, TestFileId = upload.ResponseBody?.Id ?? string.Empty };
    }

    public async Task<string?> UploadFileToFoldersAsync(string fileName, byte[] content, string contentType, IReadOnlyList<string> folderNames, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return null;
        using var driveService = await CreateDriveServiceAsync(cancellationToken);
        var parentId = _options.FolderId;
        foreach (var folderName in folderNames.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            parentId = await GetOrCreateFolderAsync(driveService, parentId, folderName.Trim(), cancellationToken);
        }

        await using var uploadStream = new MemoryStream(content);
        var request = driveService.Files.Create(new GoogleFile { Name = fileName, Parents = [parentId] }, uploadStream, contentType);
        request.Fields = "id";
        var result = await request.UploadAsync(cancellationToken);
        if (result.Exception is not null) throw result.Exception;
        return request.ResponseBody?.Id;
    }

    public async Task<string?> UploadFileAsync(string fileName, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return null;
        using var driveService = await CreateDriveServiceAsync(cancellationToken);
        await using var uploadStream = new MemoryStream(content);
        var request = driveService.Files.Create(new GoogleFile { Name = fileName, Parents = [_options.FolderId] }, uploadStream, contentType);
        request.Fields = "id";
        var result = await request.UploadAsync(cancellationToken);
        if (result.Exception is not null) throw result.Exception;
        return request.ResponseBody?.Id;
    }

    private static async Task<string> GetOrCreateFolderAsync(DriveService driveService, string parentId, string folderName, CancellationToken cancellationToken)
    {
        var escapedName = folderName.Replace("'", "\\'");
        var list = driveService.Files.List();
        list.Q = $"'{parentId}' in parents and name = '{escapedName}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        list.Fields = "files(id)";
        var existing = await list.ExecuteAsync(cancellationToken);
        if (existing.Files?.FirstOrDefault()?.Id is { Length: > 0 } existingId) return existingId;

        var created = await driveService.Files.Create(new GoogleFile { Name = folderName, MimeType = "application/vnd.google-apps.folder", Parents = [parentId] }).ExecuteAsync(cancellationToken);
        return created.Id ?? throw new InvalidOperationException($"Google Drive did not return an ID for folder '{folderName}'.");
    }

    private async Task<DriveService> CreateDriveServiceAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();
        var user = await GetCurrentUserAsync();
        var accessToken = await _userManager.GetAuthenticationTokenAsync(user, GoogleDefaults.AuthenticationScheme, "access_token");
        var refreshToken = await _userManager.GetAuthenticationTokenAsync(user, GoogleDefaults.AuthenticationScheme, "refresh_token");
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("Connect your Google account before using Google Drive.");

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = _options.ClientId, ClientSecret = _options.ClientSecret },
            Scopes = [DriveService.Scope.Drive],
            DataStore = new NullDataStore()
        });
        var credential = new UserCredential(flow, user.Id, new TokenResponse { AccessToken = accessToken, RefreshToken = refreshToken });
        await credential.RefreshTokenAsync(cancellationToken);
        await _userManager.SetAuthenticationTokenAsync(user, GoogleDefaults.AuthenticationScheme, "access_token", credential.Token.AccessToken);
        return new DriveService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = _options.ApplicationName });
    }

    private async Task<IdentityUser> GetCurrentUserAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User ?? throw new InvalidOperationException("Sign in with Google before using Google Drive.");
        return await _userManager.GetUserAsync(principal) ?? throw new InvalidOperationException("Sign in with Google before using Google Drive.");
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret)) throw new InvalidOperationException("GoogleDrive:ClientId and GoogleDrive:ClientSecret must be configured.");
        if (string.IsNullOrWhiteSpace(_options.FolderId)) throw new InvalidOperationException("GoogleDrive:FolderId must be configured.");
    }
}
