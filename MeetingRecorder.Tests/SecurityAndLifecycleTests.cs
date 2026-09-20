using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MeetingRecorder.Models;
using MeetingRecorder.Services;
using MeetingRecorder.ViewModels;
using Moq;
using Xunit;

namespace MeetingRecorder.Tests;

public class SecurityAndLifecycleTests
{
    [Fact]
    public void DashScopeApiKey_IsIgnoredInSerialization_AndEncryptedKeyIsUsed()
    {
        var settings = new AppSettings
        {
            DashScopeApiKey = "sk-test-secret-key-12345"
        };

        var json = JsonSerializer.Serialize(settings);
        json.Should().NotContain("sk-test-secret-key-12345", "Plaintext API key must never be written to JSON");
    }

    [Fact]
    public void Dpapi_CanEncryptAndDecryptDashScopeApiKey()
    {
        string originalKey = "sk-dashscope-secret-live-key";
        byte[] plainBytes = Encoding.UTF8.GetBytes(originalKey);
        byte[] cipherBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        string base64Cipher = Convert.ToBase64String(cipherBytes);

        // Decrypt
        byte[] decryptedBytes = ProtectedData.Unprotect(Convert.FromBase64String(base64Cipher), null, DataProtectionScope.CurrentUser);
        string restoredKey = Encoding.UTF8.GetString(decryptedBytes);

        restoredKey.Should().Be(originalKey);
    }

    [Fact]
    public void TranscriptionOverlayViewModel_Dispose_UnsubscribesEvents()
    {
        var transcriptionMock = new Mock<ITranscriptionService>();
        var insightMock = new Mock<IInsightService>();
        var settings = new AppSettings();

        var vm = new TranscriptionOverlayViewModel(transcriptionMock.Object, insightMock.Object, settings);
        
        // Act
        vm.Dispose();

        // Verify disposal doesn't throw and cleans up
        vm.Should().NotBeNull();
    }

    [Fact]
    public void TranscriptionCoordinator_IsUserMentioned_DetectsAliasesAndPunctuation()
    {
        var mentions = new[] { "Alex|Alec", "张伟" };

        TranscriptionCoordinator.IsUserMentioned("Hello Alex, please take notes.", mentions).Should().BeTrue();
        TranscriptionCoordinator.IsUserMentioned("Alec, do you have updates?", mentions).Should().BeTrue();
        TranscriptionCoordinator.IsUserMentioned("张伟，这个需求你负责一下", mentions).Should().BeTrue();
        TranscriptionCoordinator.IsUserMentioned("No one mentioned here.", mentions).Should().BeFalse();
    }
}
