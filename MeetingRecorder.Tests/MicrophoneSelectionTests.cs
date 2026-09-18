using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using MeetingRecorder.Models;
using MeetingRecorder.Services;
using MeetingRecorder.ViewModels;
using Moq;
using Xunit;

namespace MeetingRecorder.Tests;

[Collection("Sequential")]
public class MicrophoneSelectionTests : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Mock<IAudioRecorder> _recorderMock;
    private readonly Mock<IAudioSessionMonitor> _monitorMock;
    private readonly SessionCoordinator _sessionCoordinator;
    private readonly Mock<IFileIOService> _fileIOServiceMock;
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock;
    private readonly Mock<ICloudSyncService> _cloudSyncMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ITranscriptionService> _transcriptionServiceMock;
    private readonly Mock<IInsightService> _insightServiceMock;
    private readonly Mock<IAudioDeviceService> _audioDeviceMock;
    private readonly TranscriptionOverlayViewModel _overlayViewModel;
    private readonly string _tempSettingsPath;

    public MicrophoneSelectionTests()
    {
        var culture = new System.Globalization.CultureInfo("en-US");
        Resources.Culture = culture;

        _tempSettingsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        App.SettingsFilePath = _tempSettingsPath;

        _settings = new AppSettings();
        _recorderMock = new Mock<IAudioRecorder>();
        _monitorMock = new Mock<IAudioSessionMonitor>();
        _fileIOServiceMock = new Mock<IFileIOService>();
        _dateTimeProviderMock = new Mock<IDateTimeProvider>();
        _cloudSyncMock = new Mock<ICloudSyncService>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _transcriptionServiceMock = new Mock<ITranscriptionService>();
        _insightServiceMock = new Mock<IInsightService>();
        _audioDeviceMock = new Mock<IAudioDeviceService>();
        _overlayViewModel = new TranscriptionOverlayViewModel(_transcriptionServiceMock.Object, _insightServiceMock.Object, _settings);

        _audioDeviceMock.Setup(x => x.GetAvailableMicrophones()).Returns(new List<AudioDeviceItem>
        {
            new("System Default (Headset)", ""),
            new("Headset Mic", "device-headset-id"),
            new("USB Microphone", "device-usb-id")
        });
        _audioDeviceMock.Setup(x => x.GetDefaultMicrophoneName()).Returns("Headset");

        _sessionCoordinator = new SessionCoordinator(
            _monitorMock.Object,
            _dateTimeProviderMock.Object,
            TimeSpan.FromSeconds(5),
            _settings,
            _fileIOServiceMock.Object,
            _cloudSyncMock.Object);
    }

    private MainViewModel CreateMainViewModel()
    {
        var vm = new MainViewModel(
            _settings,
            _recorderMock.Object,
            _sessionCoordinator,
            _fileIOServiceMock.Object,
            _dateTimeProviderMock.Object,
            _cloudSyncMock.Object,
            _serviceProviderMock.Object,
            _transcriptionServiceMock.Object,
            _insightServiceMock.Object,
            _overlayViewModel,
            pythonEnvSetupService: null,
            audioDeviceService: _audioDeviceMock.Object);
        vm.DeviceRefreshDebounceMs = 0;
        return vm;
    }

    [Fact]
    public void MainViewModel_PopulatesAvailableMicrophones_FromAudioDeviceService()
    {
        using var vm = CreateMainViewModel();

        vm.AvailableMicrophones.Should().HaveCount(3);
        vm.AvailableMicrophones[0].DisplayName.Should().Be("System Default (Headset)");
        vm.AvailableMicrophones[0].Id.Should().Be("");
        vm.AvailableMicrophones[1].DisplayName.Should().Be("Headset Mic");
        vm.AvailableMicrophones[1].Id.Should().Be("device-headset-id");
        vm.AvailableMicrophones[2].DisplayName.Should().Be("USB Microphone");
        vm.AvailableMicrophones[2].Id.Should().Be("device-usb-id");
    }

    [Fact]
    public void MainViewModel_DefaultSelectedMic_MatchesSettings()
    {
        _settings.MicrophoneDeviceId = "device-usb-id";

        using var vm = CreateMainViewModel();

        vm.SelectedMicrophoneDeviceId.Should().Be("device-usb-id");
    }

    [Fact]
    public void MainViewModel_InvalidDeviceIdInSettings_FallsBackToDefault()
    {
        _settings.MicrophoneDeviceId = "non-existent-id";

        using var vm = CreateMainViewModel();

        vm.SelectedMicrophoneDeviceId.Should().Be("");
    }

    [Fact]
    public void MainViewModel_ChangingSelectedMic_UpdatesSettings()
    {
        using var vm = CreateMainViewModel();

        vm.SelectedMicrophoneDeviceId = "device-headset-id";

        _settings.MicrophoneDeviceId.Should().Be("device-headset-id");
    }

    [Fact]
    public void MainViewModel_ChangingSelectedMic_WhileRecording_InvokesSwitchMicrophone()
    {
        _recorderMock.SetupGet(r => r.IsRecording).Returns(true);
        _recorderMock.Setup(r => r.SwitchMicrophone(It.IsAny<string>())).Returns(true);

        using var vm = CreateMainViewModel();

        vm.SelectedMicrophoneDeviceId = "device-usb-id";

        _recorderMock.Verify(r => r.SwitchMicrophone("device-usb-id"), Times.Once);
    }

    [Fact]
    public void MainViewModel_AudioDeviceService_DevicesChanged_TriggersRefresh()
    {
        using var vm = CreateMainViewModel();

        _audioDeviceMock.Setup(x => x.GetAvailableMicrophones()).Returns(new List<AudioDeviceItem>
        {
            new("System Default (Built-in)", ""),
            new("Built-in Mic", "device-builtin-id")
        });

        _audioDeviceMock.Raise(x => x.DevicesChanged += null, EventArgs.Empty);

        vm.AvailableMicrophones.Should().HaveCount(2);
        vm.AvailableMicrophones[0].DisplayName.Should().Be("System Default (Built-in)");
        vm.AvailableMicrophones[1].DisplayName.Should().Be("Built-in Mic");
    }

    [Fact]
    public void MainViewModel_RefreshMicrophonesCommand_ReloadsDevices()
    {
        using var vm = CreateMainViewModel();

        _audioDeviceMock.Setup(x => x.GetAvailableMicrophones()).Returns(new List<AudioDeviceItem>
        {
            new("System Default (New Mic)", ""),
            new("New Mic", "new-id")
        });

        vm.RefreshMicrophonesCommand.Execute(null);

        vm.AvailableMicrophones.Should().HaveCount(2);
        vm.AvailableMicrophones[0].DisplayName.Should().Be("System Default (New Mic)");
    }

    [Fact]
    public void MainViewModel_MicrophoneWarning_WhileRecording_UpdatesStatusText()
    {
        using var vm = CreateMainViewModel();
        vm.Status = AppStatus.Recording;

        _recorderMock.Raise(r => r.MicrophoneWarning += null, _recorderMock.Object, Resources.MicrophoneDisconnectedWarning);

        vm.StatusText.Should().Contain(Resources.MicrophoneDisconnectedWarning);
    }

    [Fact]
    public void SettingsViewModel_LoadMicrophones_UsesAudioDeviceService()
    {
        var cloudSyncMock = new Mock<ICloudSyncService>();
        var updateMock = new Mock<IUpdateService>();
        var phraseMock = new Mock<IDashScopePhraseService>();
        var pythonMock = new Mock<IPythonEnvSetupService>();

        _settings.MicrophoneDeviceId = "device-headset-id";

        var vm = new SettingsViewModel(
            _settings,
            cloudSyncMock.Object,
            _serviceProviderMock.Object,
            updateMock.Object,
            _transcriptionServiceMock.Object,
            _insightServiceMock.Object,
            phraseMock.Object,
            pythonMock.Object,
            _audioDeviceMock.Object);

        vm.LoadMicrophones();

        vm.AvailableMicrophones.Should().HaveCount(3);
        vm.SelectedMicrophoneDeviceId.Should().Be("device-headset-id");
    }

    [Fact]
    public void WasapiRecorder_SwitchMicrophone_WhenNotRecording_UpdatesSettings()
    {
        var settings = new AppSettings { MicrophoneDeviceId = "" };
        using var recorder = new WasapiRecorder(settings);

        var result = recorder.SwitchMicrophone("new-device-id");

        result.Should().BeTrue();
        settings.MicrophoneDeviceId.Should().Be("new-device-id");
    }

    [Fact]
    public async Task MainViewModel_AudioDeviceService_DevicesChanged_DebouncesRapidEvents()
    {
        using var vm = new MainViewModel(
            _settings,
            _recorderMock.Object,
            _sessionCoordinator,
            _fileIOServiceMock.Object,
            _dateTimeProviderMock.Object,
            _cloudSyncMock.Object,
            _serviceProviderMock.Object,
            _transcriptionServiceMock.Object,
            _insightServiceMock.Object,
            _overlayViewModel,
            pythonEnvSetupService: null,
            audioDeviceService: _audioDeviceMock.Object);

        vm.DeviceRefreshDebounceMs = 50;

        _audioDeviceMock.Invocations.Clear();

        // Fire 5 rapid events in succession
        for (int i = 0; i < 5; i++)
        {
            _audioDeviceMock.Raise(x => x.DevicesChanged += null, EventArgs.Empty);
        }

        // Wait for debounce window to fire
        await Task.Delay(150);

        // GetAvailableMicrophones should have been called only once instead of 5 times
        _audioDeviceMock.Verify(x => x.GetAvailableMicrophones(), Times.Once);
    }

    public void Dispose()
    {
        if (File.Exists(_tempSettingsPath))
        {
            try { File.Delete(_tempSettingsPath); } catch { }
        }
    }
}
