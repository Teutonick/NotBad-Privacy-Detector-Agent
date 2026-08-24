using PrivacyAudit.Core;

namespace PrivacyAudit.Tests;

public sealed class DiagnosticReportBuilderTests
{
    [Fact]
    public void Build_RedactsIdentityAndUsesCoarseValues()
    {
        var finding = new Finding
        {
            Path = @"C:\Users\Nikita\Projects\SuperSecret\ClientName\foo.db",
            DisplayName = "foo.db", SizeBytes = 1_234_567, ScannerId = "file-provenance",
            Category = "File Provenance", Subcategory = "LIBRARY_DEPENDENCY", ExposureScore = 67,
            MetadataJson = "{\"password\":\"do-not-leak\",\"gps\":\"55.1,37.2\"}"
        };
        var report = DiagnosticReportBuilder.Build(finding, "Wrong finding", "1.4.2", "Microsoft Windows 10.0.26100");
        Assert.Equal("USERPROFILE / directory / directory / directory / directory / directory / file.db", report.PathShape);
        Assert.Equal("1–10 MB", report.SizeRange);
        foreach (var sensitive in new[] { "Nikita", "SuperSecret", "ClientName", "do-not-leak", "55.1" })
            Assert.DoesNotContain(sensitive, report.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1–10 MB", report.Body);
        Assert.Contains("Windows 10.0", report.Body);
        Assert.DoesNotContain("26100", report.Body);
    }

    [Fact]
    public void Build_RequiresAndIncludesUserExplanationAndIssueLabels()
    {
        var finding = new Finding { Path = @"C:\file.txt", Category = "Text", Subcategory = "Rule", ScannerId = "test" };

        var report = DiagnosticReportBuilder.Build(finding, "Wrong finding", userExplanation: "It is a harmless readme.");

        Assert.StartsWith("ОПИШИТЕ СЛОВАМИ", report.Body, StringComparison.Ordinal);
        Assert.Contains("It is a harmless readme.", report.Body, StringComparison.Ordinal);
        Assert.Contains("incorrect-detection", report.Labels);
        Assert.Contains("privacy-audit", report.Labels);
        Assert.Contains("wrong-finding", report.Labels);
    }

    [Theory]
    [InlineData(0, "0–10 KB")]
    [InlineData(50_000, "10–100 KB")]
    [InlineData(5_000_000, "1–10 MB")]
    [InlineData(2_000_000_000, "1 GB or larger")]
    public void BucketSize_ReturnsPrivacyPreservingRange(long bytes, string expected) =>
        Assert.Equal(expected, DiagnosticReportBuilder.BucketSize(bytes));

    [Fact]
    public void BuildPathShape_RedactsUncServerAndShare()
    {
        var shape = DiagnosticReportBuilder.BuildPathShape(@"\\SecretServer\ClientShare\CaseName\photo.jpg");
        Assert.Equal("NETWORK_ROOT / directory / file.jpg", shape);
        Assert.DoesNotContain("SecretServer", shape);
        Assert.DoesNotContain("ClientShare", shape);
    }
}
