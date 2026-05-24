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

        using var coordinator = new SessionCoordinator(monitor.Object, clock, TimeSpan.FromSeconds(5));
        MeetingDetectedEventArgs? requestedMeeting = null;
        coordinator.RecordingRequested += (_, e) => requestedMeeting = e;

        coordinator.Start();
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("teams", "Standup"));

        coordinator.State.Should().Be(SessionState.Recording);
        requestedMeeting.Should().NotBeNull();
        requestedMeeting!.ProcessName.Should().Be("teams");
    }

    [Fact]
    public void MeetingEnded_StopsRecordingOnlyAfterDebounceWindow()
    {
        var monitor = CreateMonitor();
        var startedAt = new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var clock = new FakeDateTimeProvider(startedAt);

        using var coordinator = new SessionCoordinator(monitor.Object, clock, TimeSpan.FromSeconds(5));
        var stopEvents = 0;
        coordinator.RecordingStopped += (_, _) => stopEvents++;

        coordinator.Start();
        monitor.Raise(m => m.MeetingStarted += null, new MeetingDetectedEventArgs("zoom", "Planning"));

        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);
        coordinator.State.Should().Be(SessionState.Recording);
        stopEvents.Should().Be(0);

        clock.UtcNow = startedAt.AddSeconds(4);
        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);
        coordinator.State.Should().Be(SessionState.Recording);
        stopEvents.Should().Be(0);

        clock.UtcNow = startedAt.AddSeconds(5);
        monitor.Raise(m => m.MeetingEnded += null, EventArgs.Empty);

        coordinator.State.Should().Be(SessionState.Detecting);
        stopEvents.Should().Be(1);
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
