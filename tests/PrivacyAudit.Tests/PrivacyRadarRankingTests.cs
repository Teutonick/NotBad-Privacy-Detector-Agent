using PrivacyAudit.Core;

namespace PrivacyAudit.Tests;

public sealed class PrivacyRadarRankingTests
{
    [Fact]
    public void ConfirmedDeepSignalsRaiseRadarPriority()
    {
        var plain = new Finding { Path = "plain.txt", DisplayName = "plain.txt", ExposureScore = 40 };
        var enriched = new Finding { Path = "secret.txt", DisplayName = "secret.txt", ExposureScore = 40 };
        enriched.MetadataJson = SecretDetectionResult.InjectIntoMetadata(enriched.MetadataJson,
            new SecretDetectionResult { TotalMatches = 1, Categories = ["API key"] });

        Assert.True(PrivacyRadarRanking.Score(enriched) > PrivacyRadarRanking.Score(plain));
        Assert.Equal(1, PrivacyRadarRanking.ConfirmedSignals(enriched));
    }
}
