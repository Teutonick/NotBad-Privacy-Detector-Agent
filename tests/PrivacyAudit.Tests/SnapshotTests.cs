using PrivacyAudit.Core;
using PrivacyAudit.Storage;

namespace PrivacyAudit.Tests;

public sealed class SnapshotTests
{
    [Fact]
    public void Snapshot_RoundTripsFindingsAndTimestamp()
    {
        var directory = Path.Combine(Path.GetTempPath(), "privacy-audit-snapshot-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "last-scan.json");
        try
        {
            var savedAt = new DateTime(2026, 8, 22, 12, 30, 0, DateTimeKind.Utc);
            var finding = new Finding
            {
                ScannerId = "filesystem", Category = "Images", Path = "C:\\sample.png", DisplayName = "sample.png",
                SizeBytes = 42, ModifiedAt = savedAt, ExposureScore = 55, ExposureReasons = ["test reason"], Ignored = true
            };
            SnapshotStore.Save(path, savedAt, [finding]);
            var loaded = SnapshotStore.Load(path);
            Assert.NotNull(loaded);
            Assert.Equal(savedAt, loaded!.SavedAtUtc);
            Assert.Equal(finding.Path, loaded.Findings[0].Path);
            Assert.Equal(finding.ExposureReasons, loaded.Findings[0].ExposureReasons);
            Assert.True(loaded.Findings[0].Ignored);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
