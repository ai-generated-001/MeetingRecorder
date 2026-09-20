namespace MeetingRecorder.Models;

public record AudioDeviceItem(string DisplayName, string Id)
{
    public override string ToString() => DisplayName;
}
