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
            var context = new AuditSnapshotContext(ScanPreset.Quick, ["C:\\Users\\Sample"], savedAt.AddMinutes(-2), savedAt, TimeSpan.FromMinutes(2));
            SnapshotStore.Save(path, savedAt, [finding], context);
            var loaded = SnapshotStore.Load(path);
            Assert.NotNull(loaded);
            Assert.Equal(savedAt, loaded!.SavedAtUtc);
            Assert.Equal(finding.Path, loaded.Findings[0].Path);
            Assert.Equal(finding.ExposureReasons, loaded.Findings[0].ExposureReasons);
            Assert.True(loaded.Findings[0].Ignored);
            Assert.Equal(ScanPreset.Quick, loaded.Context!.Preset);
            Assert.Equal("C:\\Users\\Sample", loaded.Context.Roots[0]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Snapshot_LoadAsync_HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SnapshotStore.LoadAsync(Path.Combine(Path.GetTempPath(), "missing-snapshot.json"), cancellationToken: cancellation.Token));
    }
}
