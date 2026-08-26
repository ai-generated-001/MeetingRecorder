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
    [InlineData("We discussed the new product roadmap.", new[] { "Alex", "Bob" }, false)]
    [InlineData("", new[] { "Alex" }, false)]
    [InlineData("Hello everyone", null, false)]
    [InlineData("Hello everyone", new[] { "" }, false)]
    public void IsUserMentioned_DetectsConfiguredNames(string text, string[]? names, bool expected)
    {
        bool result = MainViewModel.IsUserMentioned(text, names);
        result.Should().Be(expected);
    }
}
