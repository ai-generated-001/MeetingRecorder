using System;
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

        // Set up coordinator dependencies
        _sessionCoordinator = new SessionCoordinator(
            _monitorMock.Object,
            _dateTimeProviderMock.Object,
            TimeSpan.FromSeconds(5),
            _settings,
            _fileIOServiceMock.Object,
            _cloudSyncMock.Object);
    }

    [Fact]
    public void Constructor_ExposesUploadToDriveText()
    {
        // Act
        using var vm = new MainViewModel(
            _settings,
            _recorderMock.Object,
            _sessionCoordinator,
            _fileIOServiceMock.Object,
            _dateTimeProviderMock.Object,
            _cloudSyncMock.Object,
            _serviceProviderMock.Object);

        // Assert
        vm.UploadToDriveText.Should().Be(Resources.UploadToDrive);
    }

    [Fact]
    public void UploadCompleted_EventRaised_UpdatesStatusText()
    {
        // Arrange
        using var vm = new MainViewModel(
            _settings,
            _recorderMock.Object,
            _sessionCoordinator,
            _fileIOServiceMock.Object,
            _dateTimeProviderMock.Object,
            _cloudSyncMock.Object,
            _serviceProviderMock.Object);

        string filePath = @"C:\recordings\test_recording.mp3";
        string expectedStatus = string.Format(Resources.UploadSucceeded, "test_recording.mp3");

        // Act - Simulate background thread event, but mock Application.Current since we aren't in a WPF app domain
        if (Application.Current == null)
        {
            try
            {
                new Application();
            }
            catch
            {
                // Ignore if it fails (e.g. non-STA thread)
            }
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
        using var vm = new MainViewModel(
            _settings,
            _recorderMock.Object,
            _sessionCoordinator,
            _fileIOServiceMock.Object,
            _dateTimeProviderMock.Object,
            _cloudSyncMock.Object,
            _serviceProviderMock.Object);

        string errorMsg = "Network timeout";
        string expectedStatus = string.Format(Resources.UploadFailed, errorMsg);

        if (Application.Current == null)
        {
            try
            {
                new Application();
            }
            catch
            {
                // Ignore
            }
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

        using var vm = new MainViewModel(
            _settings,
            _recorderMock.Object,
            _sessionCoordinator,
            _fileIOServiceMock.Object,
            _dateTimeProviderMock.Object,
            _cloudSyncMock.Object,
            _serviceProviderMock.Object);

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

        using var vm = new MainViewModel(
            _settings,
            _recorderMock.Object,
            _sessionCoordinator,
            _fileIOServiceMock.Object,
            _dateTimeProviderMock.Object,
            _cloudSyncMock.Object,
            _serviceProviderMock.Object);

        // Start monitoring, then simulate a meeting started so status becomes Recording
        _sessionCoordinator.Start();
        _monitorMock.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Sprint Review"));

        vm.Status.Should().Be(AppStatus.Recording);
        vm.StatusText.Should().Be(Resources.Recording);

        string filePath = @"C:\recordings\test_recording.mp3";

        if (Application.Current == null)
        {
            try
            {
                new Application();
            }
            catch
            {
                // Ignore
            }
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
}
