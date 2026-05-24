using System;

namespace MeetingRecorder.Services;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateTime Now { get; }
}
