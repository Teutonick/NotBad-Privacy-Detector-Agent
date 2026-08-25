using PrivacyAudit.Core;

namespace PrivacyAudit.Tests;

public sealed class DetectionEvidenceTests
{
    [Fact]
    public void UnknownAndCompletedClearStatesAreDistinct()
    {
        var unknown = DetectionEvidenceCalculator.Summarize("{}");
        var clearMetadata = DetectionEvidenceCalculator.MarkCompleted("{}", DetectionEvidenceCalculator.Pii);
        var clear = DetectionEvidenceCalculator.Summarize(clearMetadata);

        Assert.False(unknown.HasCompletedScan);
        Assert.True(clear.HasCompletedScan);
        Assert.False(clear.HasConfirmedDetections);
    }

    [Fact]
    public void ConfirmedEvidenceRanksAboveUnknownRecentEvenWhenPrivacyRiskIsLower()
    {
        var pii = new PiiDetectionResult
        {
            Matches =
            [
                new PiiMatchItem("Email", "hidden", 0.9),
                new PiiMatchItem("Phone", "hidden", 0.9)
            ]
        };
        var metadata = PiiDetectionResult.InjectIntoMetadata("{}", pii);
        metadata = DetectionEvidenceCalculator.MarkCompleted(metadata, DetectionEvidenceCalculator.Pii);
        var confirmed = new Finding { ExposureScore = 10, MetadataJson = metadata };
        var unknownRecent = new Finding { ExposureScore = 100, Category = "Recent" };

        Assert.Equal(1, DetectionEvidenceCalculator.Summarize(metadata).ConfirmedCategoryCount);
        Assert.True(confirmed.DetectionPriorityRank > unknownRecent.DetectionPriorityRank);
        Assert.Same(confirmed, FindingPagination.Sort([unknownRecent, confirmed], nameof(Finding.DetectionPriorityRank), true).First());
    }
}
