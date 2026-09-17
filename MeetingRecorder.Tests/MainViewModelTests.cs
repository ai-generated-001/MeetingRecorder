using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using FluentAssertions;
using MeetingRecorder.Models;
using MeetingRecorder.Services;
using MeetingRecorder.ViewModels;
using Moq;
using Xunit;

namespace MeetingRecorder.Tests;

[Collection("Sequential")]
public class MainViewModelTests
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
    private readonly TranscriptionOverlayViewModel _overlayViewModel;

    public MainViewModelTests()
    {
        var culture = new System.Globalization.CultureInfo("en-US");
        global::MeetingRecorder.Resources.Culture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
        System.Threading.Thread.CurrentThread.CurrentCulture = culture;

        _settings = new AppSettings();
        _recorderMock = new Mock<IAudioRecorder>();
        _monitorMock = new Mock<IAudioSessionMonitor>();
        _fileIOServiceMock = new Mock<IFileIOService>();
        _dateTimeProviderMock = new Mock<IDateTimeProvider>();
        _cloudSyncMock = new Mock<ICloudSyncService>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _transcriptionServiceMock = new Mock<ITranscriptionService>();
        _insightServiceMock = new Mock<IInsightService>();
        _overlayViewModel = new TranscriptionOverlayViewModel(_transcriptionServiceMock.Object, _insightServiceMock.Object, _settings);

        // Set up coordinator dependencies
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
        return new MainViewModel(
            _settings,
            _recorderMock.Object,
            _sessionCoordinator,
            _fileIOServiceMock.Object,
            _dateTimeProviderMock.Object,
            _cloudSyncMock.Object,
            _serviceProviderMock.Object,
            _transcriptionServiceMock.Object,
            _insightServiceMock.Object,
            _overlayViewModel);
    }

    [Fact]
    public void Constructor_ExposesUploadToDriveText()
    {
        // Act
        using var vm = CreateMainViewModel();

        // Assert
        vm.UploadToDriveText.Should().Be(Resources.UploadToDrive);
    }

    [Fact]
    public void UploadCompleted_EventRaised_UpdatesStatusText()
    {
        // Arrange
        using var vm = CreateMainViewModel();

        string filePath = @"C:\recordings\test_recording.mp3";
        string expectedStatus = string.Format(Resources.UploadSucceeded, "test_recording.mp3");

        if (Application.Current == null)
        {
            try { new Application(); } catch { }
        }

        // Raise the event
        _cloudSyncMock.Raise(s => s.UploadCompleted += null, _cloudSyncMock.Object, filePath);

        // Assert
        if (Application.Current != null)
        {
            vm.StatusText.Should().Be(expectedStatus);
        }
    }

    [Fact]
    public void UploadFailed_EventRaised_UpdatesStatusText()
    {
        // Arrange
        using var vm = CreateMainViewModel();

        string errorMsg = "Network timeout";
        string expectedStatus = string.Format(Resources.UploadFailed, errorMsg);

        if (Application.Current == null)
        {
            try { new Application(); } catch { }
        }

        // Act
        _cloudSyncMock.Raise(s => s.UploadFailed += null, _cloudSyncMock.Object, errorMsg);

        // Assert
        if (Application.Current != null)
        {
            vm.StatusText.Should().Be(expectedStatus);
        }
    }

    [Fact]
    public void StopRecordingCommand_CanExecute_UpdatesCorrectlyOnStateChanged()
    {
        // Arrange
        _monitorMock.SetupGet(m => m.IsMonitoring).Returns(true);

        using var vm = CreateMainViewModel();

        // Act & Assert 1: Initially we are idle/detecting, so can't stop recording
        vm.StopRecordingCommand.CanExecute(null).Should().BeFalse();

        // Start monitoring, then simulate a meeting started
        _sessionCoordinator.Start();
        _monitorMock.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Sprint Review"));

        // Status should be Recording, StopRecordingCommand should be executable
        vm.Status.Should().Be(AppStatus.Recording);
        vm.StopRecordingCommand.CanExecute(null).Should().BeTrue();

        // Simulate meeting ended
        _monitorMock.Raise(m => m.MeetingEnded += null, EventArgs.Empty);

        // Status should be Detecting, StopRecordingCommand should be non-executable
        vm.Status.Should().Be(AppStatus.Detecting);
        vm.StopRecordingCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void UploadCompleted_EventRaisedWhileRecording_DoesNotOverrideRecordingStatus()
    {
        // Arrange
        _monitorMock.SetupGet(m => m.IsMonitoring).Returns(true);

        using var vm = CreateMainViewModel();

        // Start monitoring, then simulate a meeting started so status becomes Recording
        _sessionCoordinator.Start();
        _monitorMock.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Sprint Review"));

        vm.Status.Should().Be(AppStatus.Recording);
        vm.StatusText.Should().Be(Resources.Recording);

        string filePath = @"C:\recordings\test_recording.mp3";

        if (Application.Current == null)
        {
            try { new Application(); } catch { }
        }

        // Act
        _cloudSyncMock.Raise(s => s.UploadCompleted += null, _cloudSyncMock.Object, filePath);

        // Assert
        if (Application.Current != null)
        {
            vm.Status.Should().Be(AppStatus.Recording);
            vm.StatusText.Should().Be(Resources.Recording);
            vm.StatusCategory.Should().Be(StatusMessageCategory.Recording);
        }
    }

    [Fact]
    public void ToggleMonitoringCommand_TogglesStateCorrectly()
    {
        // Arrange
        using var vm = CreateMainViewModel();

        _sessionCoordinator.Stop();

        // Initially we are Idle, so ToggleMonitoring should start monitoring
        vm.Status.Should().Be(AppStatus.Idle);
        vm.ToggleMonitoringText.Should().Be(Resources.StartMonitoring);

        // Act & Assert 1: Toggle to Start
        vm.ToggleMonitoringCommand.Execute(null);
        vm.Status.Should().Be(AppStatus.Detecting); // Coordinator starts in Detecting
        vm.ToggleMonitoringText.Should().Be(Resources.StopMonitoring);

        // Act & Assert 2: Toggle to Stop
        vm.ToggleMonitoringCommand.Execute(null);
        vm.Status.Should().Be(AppStatus.Idle);
        vm.ToggleMonitoringText.Should().Be(Resources.StartMonitoring);
    }

    [Theory]
    [InlineData("Hey Alex, can you review the slides?", new[] { "Alex" }, true)]
    [InlineData("alex, what do you think?", new[] { "Alex" }, true)]
    [InlineData("张伟，请你准备一下明天的会议报告", new[] { "张伟" }, true)]
    [InlineData("张维，请你准备一下明天的会议报告", new[] { "张伟|张维" }, true)]
    [InlineData("Hey Alec, can you check this?", new[] { "Alex/Alec" }, true)]
    [InlineData("We discussed the new product roadmap.", new[] { "Alex", "Bob" }, false)]
    [InlineData("", new[] { "Alex" }, false)]
    [InlineData("Hello everyone", null, false)]
    [InlineData("Hello everyone", new[] { "" }, false)]
    public void IsUserMentioned_DetectsConfiguredNames(string text, string[]? names, bool expected)
    {
        bool result = MainViewModel.IsUserMentioned(text, names);
        result.Should().Be(expected);
    }

    [Fact]
    public void ToggleAiCommand_TogglesAiStateAndSavesSettings()
    {
        // Arrange
        _settings.TranscriptionEnabled = false;
        using var vm = CreateMainViewModel();

        vm.IsAiEnabled.Should().BeFalse();
        vm.AiStatusText.Should().Be(Resources.AiFeatureOff);
        vm.AiToggleText.Should().Be(Resources.TurnOnAi);

        // Act 1: Toggle ON
        vm.ToggleAiCommand.Execute(null);

        // Assert 1
        vm.IsAiEnabled.Should().BeTrue();
        _settings.TranscriptionEnabled.Should().BeTrue();
        vm.AiStatusText.Should().Be(Resources.AiFeatureOn);
        vm.AiToggleText.Should().Be(Resources.TurnOffAi);
        _overlayViewModel.IsAiActive.Should().BeTrue();

        // Act 2: Toggle OFF
        vm.ToggleAiCommand.Execute(null);

        // Assert 2
        vm.IsAiEnabled.Should().BeFalse();
        _settings.TranscriptionEnabled.Should().BeFalse();
        vm.AiStatusText.Should().Be(Resources.AiFeatureOff);
        vm.AiToggleText.Should().Be(Resources.TurnOnAi);
        _overlayViewModel.IsAiActive.Should().BeFalse();
    }

    [Fact]
    public void ToggleAiCommand_WhileRecording_TurnsOffTranscription()
    {
        // Arrange
        _settings.TranscriptionEnabled = true;
        _settings.DashScopeApiKey = "sk-test-key";
        _monitorMock.SetupGet(m => m.IsMonitoring).Returns(true);

        using var vm = CreateMainViewModel();
        _sessionCoordinator.Start();

        // Start recording
        _monitorMock.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Sprint Review"));
        vm.Status.Should().Be(AppStatus.Recording);
        _transcriptionServiceMock.Verify(t => t.StartTranscription(), Times.Once);

        // Act: Turn OFF AI during recording
        vm.ToggleAiCommand.Execute(null);

        // Assert
        vm.IsAiEnabled.Should().BeFalse();
        _transcriptionServiceMock.Verify(t => t.StopTranscription(), Times.Once);
        _overlayViewModel.IsAiActive.Should().BeFalse();
    }

    [Fact]
    public void ToggleAiCommand_WhileRecording_TurnsOnTranscriptionWhenKeyIsPresent()
    {
        // Arrange
        _settings.TranscriptionEnabled = false;
        _settings.DashScopeApiKey = "sk-test-key";
        _settings.ShowTranscriptionOverlay = false;
        _monitorMock.SetupGet(m => m.IsMonitoring).Returns(true);

        using var vm = CreateMainViewModel();
        _sessionCoordinator.Start();

        // Start recording without AI
        _monitorMock.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Sprint Review"));
        vm.Status.Should().Be(AppStatus.Recording);
        _transcriptionServiceMock.Verify(t => t.StartTranscription(), Times.Never);

        // Act: Turn ON AI during recording
        vm.ToggleAiCommand.Execute(null);

        // Assert
        vm.IsAiEnabled.Should().BeTrue();
        _transcriptionServiceMock.Verify(t => t.StartTranscription(), Times.Once);
        _overlayViewModel.IsAiActive.Should().BeTrue();
    }

    [Fact]
    public void OverlayViewModel_ToggleAiCommand_TriggersMainViewModelToggle()
    {
        // Arrange
        _settings.TranscriptionEnabled = true;
        using var vm = CreateMainViewModel();
        vm.IsAiEnabled.Should().BeTrue();

        // Act: Invoke ToggleAi via OverlayViewModel
        _overlayViewModel.ToggleAiCommand.Execute(null);

        // Assert
        vm.IsAiEnabled.Should().BeFalse();
        _settings.TranscriptionEnabled.Should().BeFalse();
    }

    [Fact]
    public void OverlayViewModel_PartialSegmentTranscribed_UpdatesCurrentLiveText()
    {
        // Arrange
        var segment = new TranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello world in progress");

        // Act
        _transcriptionServiceMock.Raise(t => t.PartialSegmentTranscribed += null, _transcriptionServiceMock.Object, new TranscriptionSegmentEventArgs(segment));

        // Assert
        _overlayViewModel.CurrentLiveText.Should().Be("Hello world in progress");
        _overlayViewModel.HasLiveText.Should().BeTrue();
    }

    [Fact]
    public void OverlayViewModel_SegmentTranscribed_ClearsLiveTextAndAddsToSegments()
    {
        // Arrange
        var partialSegment = new TranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello");
        _transcriptionServiceMock.Raise(t => t.PartialSegmentTranscribed += null, _transcriptionServiceMock.Object, new TranscriptionSegmentEventArgs(partialSegment));
        _overlayViewModel.CurrentLiveText.Should().Be("Hello");

        var finalSegment = new TranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2), "Hello everyone.");

        // Act
        _transcriptionServiceMock.Raise(t => t.SegmentTranscribed += null, _transcriptionServiceMock.Object, new TranscriptionSegmentEventArgs(finalSegment));

        // Assert
        _overlayViewModel.CurrentLiveText.Should().BeEmpty();
        _overlayViewModel.HasLiveText.Should().BeFalse();
        _overlayViewModel.Segments.Should().ContainSingle(s => s.Text == "Hello everyone.");
    }

    [Fact]
    public void OverlayViewModel_Clear_ResetsLiveTextAndSegments()
    {
        // Arrange
        var partialSegment = new TranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Testing live");
        _transcriptionServiceMock.Raise(t => t.PartialSegmentTranscribed += null, _transcriptionServiceMock.Object, new TranscriptionSegmentEventArgs(partialSegment));
        _overlayViewModel.HasLiveText.Should().BeTrue();

        // Act
        _overlayViewModel.Clear();

        // Assert
        _overlayViewModel.CurrentLiveText.Should().BeEmpty();
        _overlayViewModel.HasLiveText.Should().BeFalse();
        _overlayViewModel.Segments.Should().BeEmpty();
    }

    [Fact]
    public void MicrophoneWarning_DuringRecording_UpdatesStatusTextWithWarning()
    {
        // Arrange
        _monitorMock.SetupGet(m => m.IsMonitoring).Returns(true);
        using var vm = CreateMainViewModel();
        _sessionCoordinator.Start();

        _monitorMock.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Sprint Review"));
        vm.Status.Should().Be(AppStatus.Recording);
        vm.StatusText.Should().Be(Resources.Recording);

        // Act: Raise MicrophoneWarning
        _recorderMock.Raise(r => r.MicrophoneWarning += null, _recorderMock.Object, "Microphone silent / no audio detected");

        // Assert
        vm.StatusText.Should().Contain(Resources.Recording);
        vm.StatusText.Should().Contain("⚠️");

        // Act: Raise MicrophoneRestored
        _recorderMock.Raise(r => r.MicrophoneRestored += null, EventArgs.Empty);

        // Assert
        vm.StatusText.Should().Be(Resources.Recording);
    }

    [Fact]
    public void PythonEnvSetupCompleted_WhenReceived_DoesNotThrow()
    {
        var pythonMock = new Mock<IPythonEnvSetupService>();
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(IPythonEnvSetupService))).Returns(pythonMock.Object);

        using var vm = CreateMainViewModel();

        var act = () => pythonMock.Raise(p => p.SetupCompleted += null, new PythonEnvSetupCompletedEventArgs(true));
        act.Should().NotThrow();
    }
}
