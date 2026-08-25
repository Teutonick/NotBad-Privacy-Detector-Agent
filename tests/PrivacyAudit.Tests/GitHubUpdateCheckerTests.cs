namespace PrivacyAudit.Tests;

public sealed class GitHubUpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3-beta+build.7", 1, 2, 3)]
    [InlineData(" 2.0 ", 2, 0, 0)]
    public void ParsesReleaseTagsWithoutPrereleaseSuffix(string value, int major, int minor, int build)
    {
        Assert.True(GitHubUpdateChecker.TryParseVersion(value, out var version));
        Assert.Equal(new Version(major, minor, build, 0), version);
    }

    [Theory]
    [InlineData(1, 0, 1, 1, 0, 0, true)]
    [InlineData(1, 0, 0, 1, 0, 0, false)]
    [InlineData(0, 9, 9, 1, 0, 0, false)]
    public void UpdateResultComparesNormalizedVersions(int latestMajor, int latestMinor, int latestBuild, int currentMajor, int currentMinor, int currentBuild, bool expected)
    {
        var result = new UpdateCheckResult(new(currentMajor, currentMinor, currentBuild, 0), new(latestMajor, latestMinor, latestBuild, 0), "tag", GitHubUpdateChecker.ReleasesUrl);
        Assert.Equal(expected, result.IsUpdateAvailable);
    }
}
