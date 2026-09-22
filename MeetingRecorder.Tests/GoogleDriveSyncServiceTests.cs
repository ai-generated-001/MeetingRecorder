using System;
using System.Reflection;
using MeetingRecorder.Models;
using MeetingRecorder.Services;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace MeetingRecorder.Tests;

public class GoogleDriveSyncServiceTests
{
    [Fact]
    public void ResolveClientSecrets_WhenSettingsHaveCredentials_UsesSettingsCredentials()
    {
        var settings = new AppSettings
        {
            GoogleClientId = "UserClientId",
            GoogleClientSecret = "UserClientSecret"
        };
        using var service = new GoogleDriveSyncService(settings);

        var method = typeof(GoogleDriveSyncService).GetMethod("ResolveClientSecrets", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var result = method!.Invoke(service, null);
        result.Should().NotBeNull();
        var secrets = result as Google.Apis.Auth.OAuth2.ClientSecrets;
        secrets.Should().NotBeNull();
        secrets!.ClientId.Should().Be("UserClientId");
        secrets.ClientSecret.Should().Be("UserClientSecret");
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

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes_WithoutThrowing()
    {
        var settings = new AppSettings();
        var service = new GoogleDriveSyncService(settings);

        var act = () =>
        {
            service.Dispose();
            service.Dispose();
            service.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void ServiceProvider_DisposingMultipleRegistrationsOfGoogleDriveSyncService_DoesNotThrow()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var settings = new AppSettings();
        services.AddSingleton(settings);
        services.AddSingleton<GoogleDriveSyncService>(sp =>
            new GoogleDriveSyncService(sp.GetRequiredService<AppSettings>()));
        services.AddSingleton<ICloudSyncService>(sp => sp.GetRequiredService<GoogleDriveSyncService>());
        services.AddSingleton<IGoogleDriveAccountService>(sp => sp.GetRequiredService<GoogleDriveSyncService>());

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<GoogleDriveSyncService>();
        _ = provider.GetRequiredService<ICloudSyncService>();
        _ = provider.GetRequiredService<IGoogleDriveAccountService>();

        var act = () => provider.Dispose();
        act.Should().NotThrow();
    }
}
