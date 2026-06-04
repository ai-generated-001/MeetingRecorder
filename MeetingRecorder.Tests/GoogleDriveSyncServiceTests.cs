using System;
using System.Reflection;
using MeetingRecorder.Models;
using MeetingRecorder.Services;
using FluentAssertions;
using Xunit;

namespace MeetingRecorder.Tests;

public class GoogleDriveSyncServiceTests
{
    [Fact]
    public void ResolveClientSecrets_WhenSettingsHaveCredentials_IgnoresSettingsAndFallsBackToAssemblyMetadataOrThrows()
    {
        var settings = new AppSettings
        {
            GoogleClientId = "UserClientId",
            GoogleClientSecret = "UserClientSecret"
        };
        using var service = new GoogleDriveSyncService(settings);

        var method = typeof(GoogleDriveSyncService).GetMethod("ResolveClientSecrets", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        try
        {
            var result = method!.Invoke(service, null);
            if (result != null)
            {
                var secrets = result as Google.Apis.Auth.OAuth2.ClientSecrets;
                secrets.Should().NotBeNull();
                secrets!.ClientId.Should().NotBe("UserClientId");
                secrets.ClientSecret.Should().NotBe("UserClientSecret");
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            ex.InnerException.Message.Should().Contain("No Google OAuth credentials found");
        }
    }

    [Fact]
    public void ResolveClientSecrets_WhenSettingsAreEmpty_FallsBackToAssemblyMetadataOrThrows()
    {
        var settings = new AppSettings();
        using var service = new GoogleDriveSyncService(settings);

        var method = typeof(GoogleDriveSyncService).GetMethod("ResolveClientSecrets", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        try
        {
            var result = method!.Invoke(service, null);
            if (result != null)
            {
                var secrets = result as Google.Apis.Auth.OAuth2.ClientSecrets;
                secrets.Should().NotBeNull();
                secrets!.ClientId.Should().NotBeNullOrWhiteSpace();
                secrets.ClientSecret.Should().NotBeNullOrWhiteSpace();
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            ex.InnerException.Message.Should().Contain("No Google OAuth credentials found");
        }
    }
}
