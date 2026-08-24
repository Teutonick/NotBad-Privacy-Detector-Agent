using PrivacyAudit.Core;
using PrivacyAudit.Storage;

namespace PrivacyAudit.Tests;

public sealed class CleanupTests
{
    [Fact]
    public void SecondaryCleanupPreservesPersonalModelAndRatings()
    {
        var allowed = Path.Combine(Path.GetTempPath(), "privacy-cleanup-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(allowed, AppDataCleanupService.AppDataFolderName);
        Directory.CreateDirectory(Path.Combine(root, "Models", "YuNet"));
        Directory.CreateDirectory(Path.Combine(root, "Models", "Personal"));
        try
        {
            var db = new AuditDatabase(Path.Combine(root, "privacy-audit.db"));
            var finding = new Finding { Path = Path.Combine(root, "rated.txt"), DisplayName = "rated.txt" };
            db.SetPersonalFeedback(finding, true);
            db.Save(Guid.NewGuid(), DateTime.UtcNow, [finding]);
            File.WriteAllText(Path.Combine(root, "last-scan.json"), "{}");
            File.WriteAllText(Path.Combine(root, "crash.log"), "log");
            File.WriteAllText(Path.Combine(root, "Models", "YuNet", "model.onnx"), "model");
            File.WriteAllText(Path.Combine(root, "Models", "Personal", "attention-model.zip"), "personal");

            var cleanup = new AppDataCleanupService(root, allowed);
            cleanup.ClearCachesAndAuditResults();

            Assert.False(File.Exists(Path.Combine(root, "last-scan.json")));
            Assert.False(Directory.Exists(Path.Combine(root, "Models", "YuNet")));
            Assert.True(File.Exists(Path.Combine(root, "Models", "Personal", "attention-model.zip")));
            Assert.Single(db.GetPersonalFeedback());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(allowed)) Directory.Delete(allowed, true); }
    }

    [Fact]
    public void FullCleanupRemovesOnlyOwnedRootAndDoesNotFollowJunctionLikeScope()
    {
        var allowed = Path.Combine(Path.GetTempPath(), "privacy-full-cleanup-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(allowed, AppDataCleanupService.AppDataFolderName);
        Directory.CreateDirectory(Path.Combine(root, "Models", "Personal"));
        File.WriteAllText(Path.Combine(root, "Models", "Personal", "attention-model.zip"), "personal");
        var cleanup = new AppDataCleanupService(root, allowed);
        cleanup.DeleteAllApplicationData();
        Assert.False(Directory.Exists(root));
        Assert.True(Directory.Exists(allowed));
        Directory.Delete(allowed);
    }

    [Fact]
    public void CleanupRejectsUnexpectedDirectory()
    {
        var allowed = Path.Combine(Path.GetTempPath(), "privacy-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(allowed);
        try
        {
            Assert.Throws<ArgumentException>(() => new AppDataCleanupService(Path.Combine(allowed, "OtherApp"), allowed));
        }
        finally { Directory.Delete(allowed); }
    }
}
