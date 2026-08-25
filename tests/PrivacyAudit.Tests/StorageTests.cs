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

    [Fact]
    public void StartingOverClearsAuditResultsButKeepsLocalKnowledge()
    {
        var finding = new Finding { ScannerId = "test", Path = "C:\\sample.txt", DisplayName = "sample.txt" };
        _db.Save(Guid.NewGuid(), DateTime.UtcNow, [finding]);
        _db.AddExclusion("C:\\excluded");
        _db.SetPersonalFeedback(finding, true);

        _db.DeleteAuditResults();

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_root, "audit.db")}");
        connection.Open();
        static long Count(Microsoft.Data.Sqlite.SqliteConnection connection, string table)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table}";
            return (long)command.ExecuteScalar()!;
        }
        Assert.Equal(0, Count(connection, "scans"));
        Assert.Equal(0, Count(connection, "findings"));
        Assert.Equal(1, Count(connection, "exclusions"));
        Assert.Equal(1, Count(connection, "ml_feedback"));
    }

    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
