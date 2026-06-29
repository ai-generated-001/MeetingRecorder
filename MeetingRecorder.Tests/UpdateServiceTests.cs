using System;
using FluentAssertions;
using MeetingRecorder.Services;
using Xunit;

namespace MeetingRecorder.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3, -1)]
    [InlineData("1.2.3", 1, 2, 3, -1)]
    [InlineData("v1.2.3.4", 1, 2, 3, 4)]
    [InlineData("v1.2", 1, 2, -1, -1)]
    [InlineData("v1.2.3-beta", 1, 2, 3, -1)]
    [InlineData("  v2.5.1  ", 2, 5, 1, -1)]
    public void ParseVersion_ValidTags_ShouldParseCorrectly(string tag, int expectedMajor, int expectedMinor, int expectedBuild, int expectedRevision)
    {
        // Act
        var version = GitHubUpdateService.ParseVersion(tag);

        // Assert
        version.Should().NotBeNull();
        version!.Major.Should().Be(expectedMajor);
        version.Minor.Should().Be(expectedMinor);
        version.Build.Should().Be(expectedBuild);
        version.Revision.Should().Be(expectedRevision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("vabc")]
    public void ParseVersion_InvalidTags_ShouldReturnNull(string? tag)
    {
        // Act
        var version = GitHubUpdateService.ParseVersion(tag!);

        // Assert
        version.Should().BeNull();
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]         // Equal
    [InlineData("1.0.0.0", "1.0.0", 0)]       // Equal after normalization
    [InlineData("1.0.0", "1.0.0.0", 0)]       // Equal after normalization
    [InlineData("1.1.0", "1.0.0", 1)]         // v1 > v2
    [InlineData("1.0.0", "1.1.0", -1)]        // v1 < v2
    [InlineData("1.0.0.1", "1.0.0.0", 1)]     // v1 > v2 revision
    [InlineData("1.0.0", "1.0.0.1", -1)]      // v1 < v2 revision (v1 has no revision, normalized to 0, v2 has 1)
    [InlineData("2.0", "1.9.9", 1)]           // v1 > v2 major
    public void CompareVersions_ShouldReturnExpectedComparisonResult(string versionString1, string versionString2, int expectedComparisonSign)
    {
        // Arrange
        var v1 = Version.Parse(versionString1);
        var v2 = Version.Parse(versionString2);

        // Act
        int result = GitHubUpdateService.CompareVersions(v1, v2);

        // Assert
        if (expectedComparisonSign == 0)
        {
            result.Should().Be(0);
        }
        else if (expectedComparisonSign > 0)
        {
            result.Should().BeGreaterThan(0);
        }
        else
        {
            result.Should().BeLessThan(0);
        }
    }
}
