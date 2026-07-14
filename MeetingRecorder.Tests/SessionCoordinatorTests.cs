using System;
using FluentAssertions;
using MeetingRecorder.Models;
using MeetingRecorder.Services;
using Moq;

namespace MeetingRecorder.Tests;

public class SessionCoordinatorTests
{
    [Fact]
    public void MeetingStarted_WhenDetected_TransitionsFromIdleToRecording()
    {
        var monitor = CreateMonitor();
        var clock = new FakeDateTimeProvider(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var settings = new AppSettings();
        var fileIOService = new Mock<IFileIOService>();
        var cloudSyncService = new Mock<ICloudSyncService>();

        using var coordinator = new SessionCoordinator(
            monitor.Object, 
            clock, 
            TimeSpan.FromSeconds(5),
            settings,
            fileIOService.Object,
            cloudSyncService.Object);

        RecordingRequestedEventArgs? requestedMeeting = null;
        coordinator.RecordingRequested += (_, e) => requestedMeeting = e;

        coordinator.Start();
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("teams", "Standup"));

        coordinator.State.Should().Be(SessionState.Recording);
        requestedMeeting.Should().NotBeNull();
        requestedMeeting!.MeetingDetails.ProcessName.Should().Be("teams");
    }

    [Fact]
    public void MeetingEnded_StopsRecordingImmediately()
    {
        var monitor = CreateMonitor();
        var clock = new FakeDateTimeProvider(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var settings = new AppSettings { DebounceSeconds = 5 };
        var fileIOService = new Mock<IFileIOService>();
        var cloudSyncService = new Mock<ICloudSyncService>();

        using var coordinator = new SessionCoordinator(
            monitor.Object, 
            clock, 
            TimeSpan.FromSeconds(5),
            settings,
            fileIOService.Object,
            cloudSyncService.Object);

        var stopEvents = 0;
        coordinator.RecordingStopped += (_, _) => stopEvents++;

        coordinator.Start();
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Planning"));

        clock.UtcNow = clock.UtcNow.AddSeconds(16);
        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);

        coordinator.State.Should().Be(SessionState.Detecting);
        stopEvents.Should().Be(1);
    }

    [Fact]
    public void MeetingEnded_WhenGoogleDriveEnabled_EnqueuesUpload()
    {
        var monitor = CreateMonitor();
        var clock = new FakeDateTimeProvider(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var settings = new AppSettings { GoogleDriveEnabled = true, DebounceSeconds = 5 };
        var fileIOService = new Mock<IFileIOService>();
        fileIOService.Setup(f => f.GetFileSize(It.IsAny<string>())).Returns(2 * 1024 * 1024);
        var cloudSyncService = new Mock<ICloudSyncService>();

        using var coordinator = new SessionCoordinator(
            monitor.Object,
            clock,
            TimeSpan.FromSeconds(5),
            settings,
            fileIOService.Object,
            cloudSyncService.Object);

        coordinator.Start();
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Sprint Review"));

        clock.UtcNow = clock.UtcNow.AddSeconds(16);
        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);

        cloudSyncService.Verify(s => s.EnqueueUpload(It.IsAny<string>()), Times.Once,
            "upload must be enqueued when the meeting ends and Google Drive is enabled");
    }

    [Fact]
    public void Stop_WhileSaving_TransitionsToIdleWithoutSkippingUpload()
    {
        var monitor = CreateMonitor();
        var clock = new FakeDateTimeProvider(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var settings = new AppSettings { GoogleDriveEnabled = true, DebounceSeconds = 5 };
        var fileIOService = new Mock<IFileIOService>();
        fileIOService.Setup(f => f.GetFileSize(It.IsAny<string>())).Returns(2 * 1024 * 1024);
        var cloudSyncService = new Mock<ICloudSyncService>();

        using var coordinator = new SessionCoordinator(
            monitor.Object,
            clock,
            TimeSpan.FromSeconds(5),
            settings,
            fileIOService.Object,
            cloudSyncService.Object);

        // Hook RecordingStopped to call Stop() while still in Saving state,
        // simulating the race where Stop() is invoked mid-save.
        coordinator.RecordingStopped += (_, _) =>
        {
            // At this point the coordinator is in Saving state.
            // Calling Stop() must not skip the idle transition.
            coordinator.Stop();
        };

        coordinator.Start();
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("teams", "Retro"));

        clock.UtcNow = clock.UtcNow.AddSeconds(16);
        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);

        coordinator.State.Should().Be(SessionState.Idle,
            "after Stop() is called the coordinator must be Idle, not stuck in Saving/Detecting");
        // Upload should still have been enqueued (before Stop() was called)
        cloudSyncService.Verify(s => s.EnqueueUpload(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void MeetingEnded_WhenActiveDurationUnder10Seconds_DeletesFileAndDoesNotUpload()
    {
        var monitor = CreateMonitor();
        var clock = new FakeDateTimeProvider(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var settings = new AppSettings { DebounceSeconds = 5, GoogleDriveEnabled = true };
        var fileIOService = new Mock<IFileIOService>();
        var cloudSyncService = new Mock<ICloudSyncService>();

        using var coordinator = new SessionCoordinator(
            monitor.Object,
            clock,
            TimeSpan.FromSeconds(5),
            settings,
            fileIOService.Object,
            cloudSyncService.Object);

        coordinator.Start();
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Short Meeting"));

        // Advance clock by 12 seconds: active duration = 12 - 5 = 7 seconds (< 10)
        clock.UtcNow = clock.UtcNow.AddSeconds(12);
        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);

        fileIOService.Verify(f => f.DeleteFile(It.IsAny<string>()), Times.Once,
            "short recording file must be deleted");
        cloudSyncService.Verify(s => s.EnqueueUpload(It.IsAny<string>()), Times.Never,
            "short recording must not be uploaded");
    }

    [Fact]
    public void MeetingEnded_WhenActiveDurationOver10Seconds_KeepsFileAndUploads()
    {
        var monitor = CreateMonitor();
        var clock = new FakeDateTimeProvider(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var settings = new AppSettings { DebounceSeconds = 5, GoogleDriveEnabled = true };
        var fileIOService = new Mock<IFileIOService>();
        fileIOService.Setup(f => f.GetFileSize(It.IsAny<string>())).Returns(2 * 1024 * 1024);
        var cloudSyncService = new Mock<ICloudSyncService>();

        using var coordinator = new SessionCoordinator(
            monitor.Object,
            clock,
            TimeSpan.FromSeconds(5),
            settings,
            fileIOService.Object,
            cloudSyncService.Object);

        coordinator.Start();
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Long Meeting"));

        // Advance clock by 16 seconds: active duration = 16 - 5 = 11 seconds (>= 10)
        clock.UtcNow = clock.UtcNow.AddSeconds(16);
        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);

        fileIOService.Verify(f => f.DeleteFile(It.IsAny<string>()), Times.Never,
            "long recording file must not be deleted");
        cloudSyncService.Verify(s => s.EnqueueUpload(It.IsAny<string>()), Times.Once,
            "long recording must be uploaded");
    }

    [Fact]
    public void StopRecordingManually_DoesNotSubtractDebounceAndKeepsIfOver10Seconds()
    {
        var monitor = CreateMonitor();
        var clock = new FakeDateTimeProvider(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var settings = new AppSettings { DebounceSeconds = 15, GoogleDriveEnabled = true };
        var fileIOService = new Mock<IFileIOService>();
        fileIOService.Setup(f => f.GetFileSize(It.IsAny<string>())).Returns(2 * 1024 * 1024);
        var cloudSyncService = new Mock<ICloudSyncService>();

        using var coordinator = new SessionCoordinator(
            monitor.Object,
            clock,
            TimeSpan.FromSeconds(15),
            settings,
            fileIOService.Object,
            cloudSyncService.Object);

        coordinator.Start();
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Manual Meeting"));

        // Advance clock by 12 seconds: total duration is 12 seconds.
        // If we did not subtract debounce (since it's manual), active duration is 12 seconds (>= 10).
        // If we mistakenly subtracted debounce (15 seconds), active duration would be -3 seconds (< 10) and deleted.
        clock.UtcNow = clock.UtcNow.AddSeconds(12);
        coordinator.StopRecordingManually();

        fileIOService.Verify(f => f.DeleteFile(It.IsAny<string>()), Times.Never,
            "manually stopped recording should not subtract debounce and should be kept");
        cloudSyncService.Verify(s => s.EnqueueUpload(It.IsAny<string>()), Times.Once,
            "manually stopped recording should be uploaded");
    }

    [Fact]
    public void MeetingEnded_WhenFileSizeBelowThreshold_DeletesFileAndDoesNotUpload()
    {
        var monitor = CreateMonitor();
        var clock = new FakeDateTimeProvider(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var settings = new AppSettings { DebounceSeconds = 5, GoogleDriveEnabled = true, MinFileSizeMb = 1.5 };
        var fileIOService = new Mock<IFileIOService>();
        var cloudSyncService = new Mock<ICloudSyncService>();

        // Set file size to 1MB (which is below the 1.5MB threshold)
        fileIOService.Setup(f => f.GetFileSize(It.IsAny<string>())).Returns((long)(1.0 * 1024 * 1024));

        using var coordinator = new SessionCoordinator(
            monitor.Object,
            clock,
            TimeSpan.FromSeconds(5),
            settings,
            fileIOService.Object,
            cloudSyncService.Object);

        coordinator.Start();
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Size Test"));

        // Advance clock by 20 seconds so duration check passes (active duration: 15s)
        clock.UtcNow = clock.UtcNow.AddSeconds(20);
        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);

        fileIOService.Verify(f => f.DeleteFile(It.IsAny<string>()), Times.Once,
            "recording file below threshold size must be deleted");
        cloudSyncService.Verify(s => s.EnqueueUpload(It.IsAny<string>()), Times.Never,
            "recording file below threshold size must not be uploaded");
    }

    [Fact]
    public void MeetingEnded_WhenFileSizeAboveThreshold_KeepsFileAndUploads()
    {
        var monitor = CreateMonitor();
        var clock = new FakeDateTimeProvider(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var settings = new AppSettings { DebounceSeconds = 5, GoogleDriveEnabled = true, MinFileSizeMb = 1.5 };
        var fileIOService = new Mock<IFileIOService>();
        var cloudSyncService = new Mock<ICloudSyncService>();

        // Set file size to 2MB (which is above the 1.5MB threshold)
        fileIOService.Setup(f => f.GetFileSize(It.IsAny<string>())).Returns((long)(2.0 * 1024 * 1024));

        using var coordinator = new SessionCoordinator(
            monitor.Object,
            clock,
            TimeSpan.FromSeconds(5),
            settings,
            fileIOService.Object,
            cloudSyncService.Object);

        coordinator.Start();
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Size Test 2"));

        // Advance clock by 20 seconds so duration check passes (active duration: 15s)
        clock.UtcNow = clock.UtcNow.AddSeconds(20);
        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);

        fileIOService.Verify(f => f.DeleteFile(It.IsAny<string>()), Times.Never,
            "recording file above threshold size must not be deleted");
        cloudSyncService.Verify(s => s.EnqueueUpload(It.IsAny<string>()), Times.Once,
            "recording file above threshold size must be uploaded");
    }

    private static Mock<IAudioSessionMonitor> CreateMonitor()
    {
        var isMonitoring = false;
        var monitor = new Mock<IAudioSessionMonitor>();

        monitor.SetupGet(x => x.IsMonitoring).Returns(() => isMonitoring);
        monitor.Setup(x => x.StartMonitoring()).Callback(() => isMonitoring = true);
        monitor.Setup(x => x.StopMonitoring()).Callback(() => isMonitoring = false);
        monitor.Setup(x => x.GetCurrentActiveMeeting()).Returns((MeetingDetectedEventArgs?)null);

        return monitor;
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public FakeDateTimeProvider(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; set; }

        public DateTime Now => UtcNow.ToLocalTime();
    }
}
