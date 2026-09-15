using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MeetingRecorder.Services;
using Xunit;

namespace MeetingRecorder.Tests;

public class PythonEnvSetupServiceTests
{
    [Fact]
    public void GetVenvScriptsDirectory_ReturnsValidPathUnderAppFolder()
    {
        var scriptsDir = PythonEnvSetupService.GetVenvScriptsDirectory();
        scriptsDir.Should().NotBeNullOrWhiteSpace();
        scriptsDir.Should().Contain("python_env");
    }

    [Fact]
    public void GetVenvPythonExecutable_ReturnsValidExecutablePath()
    {
        var exe = PythonEnvSetupService.GetVenvPythonExecutable();
        exe.Should().NotBeNullOrWhiteSpace();
        if (OperatingSystem.IsWindows())
        {
            exe.Should().EndWith("python.exe");
        }
        else
        {
            exe.Should().EndWith("python");
        }
    }

    [Fact]
    public async Task FindHostPythonExecutableAsync_DetectsPythonIfAvailable()
    {
        var service = new PythonEnvSetupService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var host = await service.FindHostPythonExecutableAsync(cts.Token);

        // If Python or py launcher is present on the machine, it should return a non-empty name
        // (If not installed on a bare CI machine, it returns null without throwing)
        if (host != null)
        {
            host.Should().BeOneOf("py", "python", "python3");
        }
    }

    [Fact]
    public void IsVenvReady_WhenVenvDoesNotExist_ReturnsFalse()
    {
        var service = new PythonEnvSetupService();
        // Pointing to a non-existent directory
        var originalPath = App.PythonEnvFolderPath;
        try
        {
            App.PythonEnvFolderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            service.IsVenvReady().Should().BeFalse();
        }
        finally
        {
            App.PythonEnvFolderPath = originalPath;
        }
    }

    [Fact]
    public void FindNotebookLmExecutable_PrefersManagedVenvIfExists()
    {
        var tempVenv = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var scriptsDir = OperatingSystem.IsWindows()
            ? Path.Combine(tempVenv, "Scripts")
            : Path.Combine(tempVenv, "bin");
        Directory.CreateDirectory(scriptsDir);

        var dummyExe = OperatingSystem.IsWindows()
            ? Path.Combine(scriptsDir, "notebooklm.exe")
            : Path.Combine(scriptsDir, "notebooklm");
        File.WriteAllText(dummyExe, "");

        var originalPath = App.PythonEnvFolderPath;
        try
        {
            App.PythonEnvFolderPath = tempVenv;
            var resolved = NotebookLmSyncService.FindNotebookLmExecutable("");
            resolved.Should().Be(dummyExe);
        }
        finally
        {
            App.PythonEnvFolderPath = originalPath;
            try { Directory.Delete(tempVenv, recursive: true); } catch { }
        }
    }

    [Fact]
    public void FindNotebookLmExecutable_PrefersExplicitConfigOverVenv()
    {
        var explicitFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_custom.exe");
        File.WriteAllText(explicitFile, "");

        try
        {
            var resolved = NotebookLmSyncService.FindNotebookLmExecutable(explicitFile);
            resolved.Should().Be(explicitFile);
        }
        finally
        {
            try { File.Delete(explicitFile); } catch { }
        }
    }
}
