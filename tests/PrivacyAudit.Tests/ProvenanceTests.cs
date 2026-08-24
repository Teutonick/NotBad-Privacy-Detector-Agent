using Microsoft.Data.Sqlite;
using PrivacyAudit.Core;
using PrivacyAudit.Storage;

namespace PrivacyAudit.Tests;

public sealed class ProvenanceTests
{
    [Fact]
    public async Task ManualInvestigationReadsOnlySelectedContextAndPersistsCurrentResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacy-audit-provenance-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "profile.db");
            using (var c = new SqliteConnection($"Data Source={path}")) { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TABLE users(id INTEGER); CREATE TABLE sessions(id INTEGER);"; cmd.ExecuteNonQuery(); }
            var info = new FileInfo(path); var finding = new Finding { Id = Guid.NewGuid(), Path = path, DisplayName = "profile.db", SizeBytes = info.Length, ModifiedAt = info.LastWriteTime, MetadataJson = "{}" };
            var result = await new FileProvenanceAnalyzer().AnalyzeAsync(finding, CancellationToken.None);
            Assert.Equal(FileProvenanceSchema.Version, result.EngineVersion);
            Assert.Equal("SQLite", result.DetectedFormat);
            Assert.Contains(result.SchemaHints, x => x.Equals("users", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("profile.db", Path.GetFileName(result.Path));

            var db = new AuditDatabase(Path.Combine(root, "audit.db")); db.SaveProvenance(result);
            Assert.NotNull(db.GetProvenance(finding));
            var changed = new Finding { Id = finding.Id, Path = finding.Path, DisplayName = finding.DisplayName, SizeBytes = finding.SizeBytes + 1, ModifiedAt = finding.ModifiedAt };
            var stale = db.GetProvenance(changed);
            Assert.NotNull(stale);
            Assert.False(stale!.IsCurrent(changed));
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
