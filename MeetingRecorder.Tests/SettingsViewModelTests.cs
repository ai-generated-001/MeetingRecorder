using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MeetingRecorder.Models;
using MeetingRecorder.Services;
using MeetingRecorder.ViewModels;
using Moq;
using Xunit;

namespace MeetingRecorder.Tests;

public class SettingsViewModelTests : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Mock<ICloudSyncService> _cloudSyncMock;
    private readonly string _tempSettingsPath;

    public SettingsViewModelTests()
    {
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        App.SettingsFilePath = _tempSettingsPath;

        _settings = new AppSettings
        {
            OutputDirectory = "Initial/Directory",
            UiLanguage = "zh-CN",
            GoogleDriveEnabled = true,
            GoogleClientId = "ClientId",
            GoogleClientSecret = "ClientSecret",
            GoogleDriveFolderPath = "DriveFolder",
            GoogleDriveFolderId = "FolderId"
        };
        _cloudSyncMock = new Mock<ICloudSyncService>();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempSettingsPath))
            {
                File.Delete(_tempSettingsPath);
            }
        }
        catch { /* best effort */ }
    }

    [Fact]
    public void Constructor_ShouldInitializePropertiesFromSettings()
    {
        // Act
        var vm = new SettingsViewModel(_settings, _cloudSyncMock.Object);

        // Assert
        vm.OutputDirectory.Should().Be("Initial/Directory");
        vm.UiLanguage.Should().Be("zh-CN");
        vm.GoogleDriveEnabled.Should().BeTrue();
        vm.GoogleClientId.Should().Be("ClientId");
        vm.GoogleClientSecret.Should().Be("ClientSecret");
        vm.GoogleDriveFolderPath.Should().Be("DriveFolder");
    }

    [Fact]
    public void SaveCommand_ShouldUpdateSettingsAndRaiseRequestClose()
    {
        // Arrange
        var vm = new SettingsViewModel(_settings, _cloudSyncMock.Object);
        vm.OutputDirectory = "New/Directory";
        vm.UiLanguage = "en-US";
        vm.GoogleDriveEnabled = false;

        bool? requestCloseResult = null;
        vm.RequestClose += (sender, result) => requestCloseResult = result;

        // Act
        vm.SaveCommand.Execute(null);

        // Assert
        _settings.OutputDirectory.Should().Be("New/Directory");
        _settings.UiLanguage.Should().Be("en-US");
        _settings.GoogleDriveEnabled.Should().BeFalse();
        requestCloseResult.Should().BeTrue();
    }

    [Fact]
    public void SaveCommand_WithCredentialsChanged_ShouldResetPersistedFolderId()
    {
        // Arrange
        var vm = new SettingsViewModel(_settings, _cloudSyncMock.Object);
        vm.GoogleDriveFolderPath = "ChangedFolder"; // Triggers credential change detection

        bool? requestCloseResult = null;
        vm.RequestClose += (sender, result) => requestCloseResult = result;

        // Act
        vm.SaveCommand.Execute(null);

        // Assert
        _settings.GoogleDriveFolderId.Should().BeEmpty();
        requestCloseResult.Should().BeTrue();
    }
}
