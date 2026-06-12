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

        var stopEvents = 0;
        coordinator.RecordingStopped += (_, _) => stopEvents++;

        coordinator.Start();
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Planning"));

        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);

        coordinator.State.Should().Be(SessionState.Detecting);
        stopEvents.Should().Be(1);
    }

    [Fact]
    public void MeetingEnded_WhenGoogleDriveEnabled_EnqueuesUpload()
    {
        var monitor = CreateMonitor();
        var clock = new FakeDateTimeProvider(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var settings = new AppSettings { GoogleDriveEnabled = true };
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
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Sprint Review"));
        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);

        cloudSyncService.Verify(s => s.EnqueueUpload(It.IsAny<string>()), Times.Once,
            "upload must be enqueued when the meeting ends and Google Drive is enabled");
    }

    [Fact]
    public void Stop_WhileSaving_TransitionsToIdleWithoutSkippingUpload()
    {
        var monitor = CreateMonitor();
        var clock = new FakeDateTimeProvider(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc));
        var settings = new AppSettings { GoogleDriveEnabled = true };
        var fileIOService = new Mock<IFileIOService>();
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
        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);

        coordinator.State.Should().Be(SessionState.Idle,
            "after Stop() is called the coordinator must be Idle, not stuck in Saving/Detecting");
        // Upload should still have been enqueued (before Stop() was called)
        cloudSyncService.Verify(s => s.EnqueueUpload(It.IsAny<string>()), Times.Once);
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
