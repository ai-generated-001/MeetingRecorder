using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using MeetingRecorder.Models;
using Xunit;

namespace MeetingRecorder.Tests;

public class SettingsPersistenceTests
{
    [Fact]
    public void AppSettings_CanBeSerializedAndDeserialized()
    {
        var settings = new AppSettings
        {
            GoogleDriveEnabled = true,
            GoogleClientId = "TestClientId",
            GoogleClientSecret = "TestClientSecret",
            GoogleDriveFolderPath = "Resolved/Work/Meetings",
            UiLanguage = "zh-CN",
            OutputFormat = OutputFormat.Wav,
            DebounceSeconds = 12
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        json.Should().Contain("TestClientId");
        json.Should().Contain("Resolved/Work/Meetings");

        var deserialized = JsonSerializer.Deserialize<AppSettings>(json);
        deserialized.Should().NotBeNull();
        deserialized!.GoogleDriveEnabled.Should().BeTrue();
        deserialized.GoogleClientId.Should().Be("TestClientId");
        deserialized.GoogleClientSecret.Should().Be("TestClientSecret");
        deserialized.GoogleDriveFolderPath.Should().Be("Resolved/Work/Meetings");
        deserialized.UiLanguage.Should().Be("zh-CN");
        deserialized.OutputFormat.Should().Be(OutputFormat.Wav);
        deserialized.DebounceSeconds.Should().Be(12);
    }
}
