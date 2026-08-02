using Microsoft.Extensions.Options;
using RLRentalApp.Web.Services;
using Xunit;

namespace RLRentalApp.Web.Tests;

public class GoogleDriveStorageServiceTests
{
    [Fact]
    public async Task UploadFileToFoldersAsync_WhenDisabled_ReportsConfigurationFailure()
    {
        var sut = CreateDisabledService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UploadFileToFoldersAsync("services.pdf", [1, 2, 3], "application/pdf", ["Properties"]));

        Assert.Contains("Google Drive is disabled", exception.Message);
        Assert.Contains("GoogleDrive:Enabled", exception.Message);
    }

    [Fact]
    public async Task UploadFileAsync_WhenDisabled_ReportsConfigurationFailure()
    {
        var sut = CreateDisabledService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UploadFileAsync("statement.pdf", [1, 2, 3], "application/pdf"));

        Assert.Contains("Google Drive is disabled", exception.Message);
    }

    private static GoogleDriveStorageService CreateDisabledService()
    {
        return new GoogleDriveStorageService(
            Options.Create(new GoogleDriveOptions { Enabled = false }),
            null!,
            null!);
    }
}
