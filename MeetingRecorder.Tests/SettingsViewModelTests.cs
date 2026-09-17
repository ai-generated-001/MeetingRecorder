using System;
using System.Collections.Generic;
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

[Collection("Sequential")]
public class SettingsViewModelTests : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Mock<ICloudSyncService> _cloudSyncMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IUpdateService> _updateServiceMock;
    private readonly Mock<ITranscriptionService> _transcriptionServiceMock;
    private readonly Mock<IInsightService> _insightServiceMock;
    private readonly Mock<IDashScopePhraseService> _phraseServiceMock;
    private readonly Mock<IPythonEnvSetupService> _pythonEnvSetupMock;
    private readonly string _tempSettingsPath;
    private readonly string _tempTokenPath;

    public SettingsViewModelTests()
    {
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        App.SettingsFilePath = _tempSettingsPath;
        _tempTokenPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_token");
        App.TokenFolderPath = _tempTokenPath;

        _settings = new AppSettings
        {
            OutputDirectory = "Initial/Directory",
            UiLanguage = "zh-CN",
            GoogleDriveEnabled = true,
            GoogleClientId = "ClientId",
            GoogleClientSecret = "ClientSecret",
            GoogleDriveFolderPath = "DriveFolder",
            GoogleDriveFolderId = "FolderId",
            StartWithWindows = true,
            MinFileSizeMb = 2.5,
            AutoCheckUpdates = true,
            Theme = "Dark",
            TranscriptionEnabled = true,
            DashScopeApiKey = "sk-test-key",
            DashScopeBaseUrl = "https://dashscope.aliyuncs.com",
            TranscriptionLanguage = "zh",
            ShowTranscriptionOverlay = false,
            VocabularyId = "voc-12345",
            Hotwords = "张伟:5, Alex:5",
            InsightsEnabled = true,
            MentionNames = new List<string> { "Alex", "张伟" },
            InsightContextSeconds = 45,
            QwenModel = "qwen-turbo"
        };
        _cloudSyncMock = new Mock<ICloudSyncService>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _updateServiceMock = new Mock<IUpdateService>();
        _transcriptionServiceMock = new Mock<ITranscriptionService>();
        _insightServiceMock = new Mock<IInsightService>();
        _phraseServiceMock = new Mock<IDashScopePhraseService>();
        _pythonEnvSetupMock = new Mock<IPythonEnvSetupService>();
    }

    private SettingsViewModel CreateViewModel()
    {
        return new SettingsViewModel(
            _settings,
            _cloudSyncMock.Object,
            _serviceProviderMock.Object,
            _updateServiceMock.Object,
            _transcriptionServiceMock.Object,
            _insightServiceMock.Object,
            _phraseServiceMock.Object,
            _pythonEnvSetupMock.Object);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempSettingsPath))
            {
                File.Delete(_tempSettingsPath);
            }
            if (Directory.Exists(_tempTokenPath))
            {
                Directory.Delete(_tempTokenPath, true);
            }
        }
        catch { /* best effort */ }
    }

    [Fact]
    public void Constructor_ShouldInitializePropertiesFromSettings()
    {
        // Act
        var vm = CreateViewModel();

        // Assert
        vm.OutputDirectory.Should().Be("Initial/Directory");
        vm.UiLanguage.Should().Be("zh-CN");
        vm.GoogleDriveEnabled.Should().BeTrue();
        vm.GoogleClientId.Should().Be("ClientId");
        vm.GoogleClientSecret.Should().Be("ClientSecret");
        vm.GoogleDriveFolderPath.Should().Be("DriveFolder");
        vm.StartWithWindows.Should().BeTrue();
        vm.MinFileSizeMb.Should().Be(2.5);
        vm.TranscriptionEnabled.Should().BeTrue();
        vm.DashScopeApiKey.Should().Be("sk-test-key");
        vm.DashScopeBaseUrl.Should().Be("https://dashscope.aliyuncs.com");
        vm.VocabularyId.Should().Be("voc-12345");
        vm.HotwordsText.Should().Be("张伟:5, Alex:5");
        vm.InsightsEnabled.Should().BeTrue();
        vm.MentionNamesText.Should().Be("Alex, 张伟");
        vm.InsightContextSeconds.Should().Be(45);
        vm.QwenModel.Should().Be("qwen-turbo");
    }

    [Fact]
    public void SaveCommand_ShouldUpdateSettingsAndRaiseRequestClose()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.OutputDirectory = "New/Directory";
        vm.UiLanguage = "en-US";
        vm.GoogleDriveEnabled = false;
        vm.StartWithWindows = false;
        vm.MinFileSizeMb = 5.0;
        vm.SelectedMicrophoneDeviceId = "custom-mic-id";
        vm.DashScopeApiKey = "sk-new-key";
        vm.MentionNamesText = "Alice, Bob, 王五";
        vm.InsightContextSeconds = 60;
        vm.QwenModel = "qwen-max";

        bool? requestCloseResult = null;
        vm.RequestClose += (sender, result) => requestCloseResult = result;

        // Act
        vm.SaveCommand.Execute(null);

        // Assert
        _settings.OutputDirectory.Should().Be("New/Directory");
        _settings.UiLanguage.Should().Be("en-US");
        _settings.GoogleDriveEnabled.Should().BeFalse();
        _settings.StartWithWindows.Should().BeFalse();
        _settings.MinFileSizeMb.Should().Be(5.0);
        _settings.MicrophoneDeviceId.Should().Be("custom-mic-id");
        _settings.DashScopeApiKey.Should().Be("sk-new-key");
        _settings.MentionNames.Should().ContainInOrder("Alice", "Bob", "王五");
        _settings.InsightContextSeconds.Should().Be(60);
        _settings.QwenModel.Should().Be("qwen-max");
        requestCloseResult.Should().BeTrue();
    }

    [Fact]
    public void SaveCommand_WithCredentialsChanged_ShouldResetPersistedFolderId()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.GoogleDriveFolderPath = "ChangedFolder"; // Triggers credential change detection

        bool? requestCloseResult = null;
        vm.RequestClose += (sender, result) => requestCloseResult = result;

        // Act
        vm.SaveCommand.Execute(null);

        // Assert
        _settings.GoogleDriveFolderId.Should().BeEmpty();
        requestCloseResult.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WhenPythonEnvIsSettingUp_InitializesWithActiveState()
    {
        _pythonEnvSetupMock.SetupGet(x => x.IsSettingUp).Returns(true);
        _pythonEnvSetupMock.SetupGet(x => x.ProgressText).Returns("Installing pip packages...");

        var vm = CreateViewModel();

        vm.IsSettingUpPythonEnv.Should().BeTrue();
        vm.PythonEnvProgressText.Should().Be("Installing pip packages...");
    }

    [Fact]
    public void SetupPythonEnvCommand_CallsStartSetupOnService()
    {
        _pythonEnvSetupMock.Setup(x => x.StartSetup()).Returns(true);

        var vm = CreateViewModel();
        vm.SetupPythonEnvCommand.Execute(null);

        _pythonEnvSetupMock.Verify(x => x.StartSetup(), Times.Once);
        vm.IsSettingUpPythonEnv.Should().BeTrue();
    }

    [Fact]
    public void Cleanup_UnsubscribesFromPythonEnvSetupEvents()
    {
        var vm = CreateViewModel();

        var act = () =>
        {
            vm.Cleanup();
            _pythonEnvSetupMock.Raise(x => x.SetupProgressChanged += null, new PythonEnvSetupProgressEventArgs("Progress"));
            _pythonEnvSetupMock.Raise(x => x.SetupCompleted += null, new PythonEnvSetupCompletedEventArgs(true));
        };

        act.Should().NotThrow();
    }
}
