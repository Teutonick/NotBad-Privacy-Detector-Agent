using PrivacyAudit.Core;
using PrivacyAudit.Storage;

namespace PrivacyAudit.Tests;

public sealed class StorageTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "PrivacyAuditTests", Guid.NewGuid().ToString("N"));
    readonly AuditDatabase _db;
    public StorageTests() { Directory.CreateDirectory(_root); _db = new AuditDatabase(Path.Combine(_root, "audit.db")); }

    [Fact]
    public void Exclusions_ArePersistedWithoutDuplicates()
    {
        var path = Path.Combine(_root, "private");
        _db.AddExclusion(path); _db.AddExclusion(path);
        Assert.Equal([path], _db.GetExclusions());
    }

    [Fact]
    public void ScanAndFindings_AreStoredLocally()
    {
        _db.Save(Guid.NewGuid(), DateTime.UtcNow, [new Finding { ScannerId = "test", Path = "C:\\sample.txt", DisplayName = "sample.txt" }]);
        Assert.True(File.Exists(Path.Combine(_root, "audit.db")));
        Assert.True(new FileInfo(Path.Combine(_root, "audit.db")).Length > 0);
    }

    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
