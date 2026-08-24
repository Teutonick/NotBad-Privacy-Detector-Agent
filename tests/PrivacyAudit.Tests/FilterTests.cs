using PrivacyAudit.Core;

namespace PrivacyAudit.Tests;

public sealed class FilterTests
{
    [Fact]
    public void MatchesSize_ZeroStep_MatchesEverything()
    {
        var findingEmpty = new Finding { DisplayName = "empty", SizeBytes = 0 };
        var findingLarge = new Finding { DisplayName = "large", SizeBytes = 100L * 1024 * 1024 };

        Assert.True(FindingFilter.MatchesSize(findingEmpty, 0));
        Assert.True(FindingFilter.MatchesSize(findingLarge, 0));
    }

    [Theory]
    [InlineData(1, 100 * 1024, true)]
    [InlineData(1, 99 * 1024, false)]
    [InlineData(3, 1024 * 1024, true)]
    [InlineData(3, 500 * 1024, false)]
    [InlineData(8, 1024L * 1024 * 1024, true)]
    [InlineData(8, 500L * 1024 * 1024, false)]
    public void MatchesSize_FiltersCorrectlyAccordingToThreshold(int step, long size, bool expected)
    {
        var finding = new Finding { DisplayName = "test", SizeBytes = size };
        Assert.Equal(expected, FindingFilter.MatchesSize(finding, step));
    }

    [Fact]
    public void MatchesAge_HandlesNullDates()
    {
        var findingNoDate = new Finding { DisplayName = "nodate", ModifiedAt = null, CreatedAt = null };
        Assert.True(FindingFilter.MatchesAge(findingNoDate, 0));
        Assert.False(FindingFilter.MatchesAge(findingNoDate, 1));
        Assert.False(FindingFilter.MatchesAge(findingNoDate, 5));
    }

    [Fact]
    public void MatchesAge_FiltersRecentAndOldFiles()
    {
        var now = new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
        var recent10Days = new Finding { DisplayName = "recent", ModifiedAt = now.AddDays(-10) };
        var recent90Days = new Finding { DisplayName = "recent3m", ModifiedAt = now.AddDays(-90) };
        var old400Days = new Finding { DisplayName = "old1y", ModifiedAt = now.AddDays(-400) };
        var old2000Days = new Finding { DisplayName = "old5y", ModifiedAt = now.AddDays(-2000) };

        // Step 0: All
        Assert.True(FindingFilter.MatchesAge(recent10Days, 0, now));
        Assert.True(FindingFilter.MatchesAge(old2000Days, 0, now));

        // Step 1: < 1 month
        Assert.True(FindingFilter.MatchesAge(recent10Days, 1, now));
        Assert.False(FindingFilter.MatchesAge(recent90Days, 1, now));
        Assert.False(FindingFilter.MatchesAge(old400Days, 1, now));

        // Step 2: < 6 months
        Assert.True(FindingFilter.MatchesAge(recent10Days, 2, now));
        Assert.True(FindingFilter.MatchesAge(recent90Days, 2, now));
        Assert.False(FindingFilter.MatchesAge(old400Days, 2, now));

        // Step 5: > 1 year
        Assert.False(FindingFilter.MatchesAge(recent10Days, 5, now));
        Assert.False(FindingFilter.MatchesAge(recent90Days, 5, now));
        Assert.True(FindingFilter.MatchesAge(old400Days, 5, now));
        Assert.True(FindingFilter.MatchesAge(old2000Days, 5, now));

        // Step 8: > 5 years
        Assert.False(FindingFilter.MatchesAge(old400Days, 8, now));
        Assert.True(FindingFilter.MatchesAge(old2000Days, 8, now));
    }

    [Fact]
    public void Keys_ReturnValidStrings()
    {
        Assert.Equal("AnySize", FindingFilter.GetSizeKey(0));
        Assert.Equal("SizeGte1MB", FindingFilter.GetSizeKey(3));
        Assert.Equal("AnyAge", FindingFilter.GetAgeKey(0));
        Assert.Equal("AgeOlder1Year", FindingFilter.GetAgeKey(5));
    }

    [Theory]
    [InlineData("True", true, 1.0)]
    [InlineData("True", false, 0.45)]
    [InlineData("True", null, 0.45)]
    [InlineData("False", false, 1.0)]
    [InlineData("False", true, 0.45)]
    [InlineData("False", null, 0.45)]
    [InlineData("Clear", true, 0.8)]
    [InlineData("Clear", false, 0.8)]
    [InlineData("Clear", null, 0.25)]
    public void FeedbackOpacityConverter_ReturnsExpectedOpacity(string parameter, bool? label, double expected)
    {
        var converter = new FeedbackOpacityConverter();
        var opacity = converter.Convert(label, typeof(double), parameter, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, (double)opacity!);
    }

    [Fact]
    public void FeedbackBackgroundConverter_ReturnsTintOnlyWhenSelected()
    {
        var converter = new FeedbackBackgroundConverter();
        var posSelected = converter.Convert(true, typeof(System.Windows.Media.Brush), "True", System.Globalization.CultureInfo.InvariantCulture);
        var posUnselected = converter.Convert(false, typeof(System.Windows.Media.Brush), "True", System.Globalization.CultureInfo.InvariantCulture);
        var negSelected = converter.Convert(false, typeof(System.Windows.Media.Brush), "False", System.Globalization.CultureInfo.InvariantCulture);
        var negUnselected = converter.Convert(true, typeof(System.Windows.Media.Brush), "False", System.Globalization.CultureInfo.InvariantCulture);

        Assert.NotEqual(posSelected, posUnselected);
        Assert.NotEqual(negSelected, negUnselected);
    }
}
