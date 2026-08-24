using Microsoft.Data.Sqlite;
using PrivacyAudit.Core;
using PrivacyAudit.Storage;

namespace PrivacyAudit.Tests;

public sealed class StorageLimitsTests
{
    [Fact]
    public void DiagnosticLogIsTrimmedToTenMegabytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacy-retention-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "diagnostic.log"); File.WriteAllBytes(path, new byte[StorageLimits.MaxDiagnosticLogBytes + 1024]);
            StorageLimits.TrimTextLog(path, StorageLimits.MaxDiagnosticLogBytes);
            Assert.True(new FileInfo(path).Length <= StorageLimits.MaxDiagnosticLogBytes);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void AuditRetentionRemovesOldScansButKeepsRecentAndFeedback()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacy-retention-db-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var dbPath = Path.Combine(root, "privacy-audit.db"); var db = new AuditDatabase(dbPath);
            var finding = new Finding { Path = Path.Combine(root, "x.txt"), DisplayName = "x.txt" };
            var oldScan = Guid.NewGuid(); db.Save(oldScan, DateTime.UtcNow.AddDays(-200), [finding]);
            var recentScan = Guid.NewGuid(); db.Save(recentScan, DateTime.UtcNow, [new Finding { Id = Guid.NewGuid(), Path = finding.Path, DisplayName = finding.DisplayName }]);
            using (var c = new SqliteConnection($"Data Source={dbPath}")) { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE scans SET finished_at=$at WHERE id=$id"; cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.AddDays(-200).ToString("O")); cmd.Parameters.AddWithValue("$id", oldScan.ToString()); cmd.ExecuteNonQuery(); }
            db.SetPersonalFeedback(finding, true); db.PruneAuditHistory(DateTime.UtcNow.AddDays(-183));
            using var check = new SqliteConnection($"Data Source={dbPath}"); check.Open(); using var count = check.CreateCommand(); count.CommandText = "SELECT COUNT(*) FROM scans";
            Assert.Equal(1L, (long)count.ExecuteScalar()!); Assert.Single(db.GetPersonalFeedback());
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
