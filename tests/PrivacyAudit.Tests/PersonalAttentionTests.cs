using PrivacyAudit.Core;
using PrivacyAudit.Storage;

namespace PrivacyAudit.Tests;

public sealed class PersonalAttentionTests
{
    [Theory]
    [InlineData(19, 10, false)]
    [InlineData(20, 4, false)]
    [InlineData(20, 5, true)]
    [InlineData(30, 5, false)]
    [InlineData(30, 6, true)]
    public void TrainingPolicyEnforcesMinimumsAndBalance(int total, int positive, bool expected) =>
        Assert.Equal(expected, new PersonalModelStats(total, positive, total - positive).CanTrain);

    [Fact]
    public void FeedbackPersistsUpdatesAndCanBeCleared()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacy-audit-ml-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var db = new AuditDatabase(Path.Combine(root, "test.db"));
            var finding = new Finding { Path = Path.Combine(root, "file.txt"), DisplayName = "file.txt", ExposureScore = 40 };
            db.SetPersonalFeedback(finding, true);
            Assert.True(db.GetPersonalFeedback().Single().Label);
            db.SetPersonalFeedback(finding, false);
            var updated = db.GetPersonalFeedback().Single(); Assert.False(updated.Label); Assert.Equal(PersonalAttentionSchema.Version, updated.FeatureSchemaVersion);
            db.SetPersonalFeedback(finding, null); Assert.Empty(db.GetPersonalFeedback());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SdcaModelTrainsSavesLoadsAndPredicts()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacy-audit-model-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var samples = Enumerable.Range(0, 24).Select(i => new PersonalAttentionFeatures
            {
                Label = i >= 12, ExposureScore = i >= 12 ? 90 : 10, LogFileSize = 3 + i % 4,
                FileAgeDays = i * 10, Extension = i >= 12 ? ".xlsx" : ".tmp", FileCategory = i >= 12 ? "Documents" : "Other",
                DirectoryCategory = "Documents", ScannerCategory = "test", PersonalDataMatches = i >= 12 ? 3 : 0
            }).ToArray();
            var service = new PersonalAttentionModelService(root);
            var metadata = await service.TrainAsync(samples, CancellationToken.None);
            Assert.Equal("SdcaLogisticRegression", metadata.ModelType); Assert.Equal(24, metadata.TrainedSamples); Assert.True(service.IsReady);
            Assert.True(File.Exists(Path.Combine(root, "Models", "Personal", "attention-model.zip")));
            Assert.True(new PersonalAttentionModelService(root).IsReady);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SdcaModelHandlesLowVarianceRealisticSamples()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacy-audit-low-variance-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var samples = Enumerable.Range(0, 26).Select(i => new PersonalAttentionFeatures
            {
                Label = i is 1 or 15 or 16 or 17 or 20 or 21 or 22 or 23 or 24 or 25,
                ExposureScore = i < 22 ? 100 : 0, Extension = i < 22 ? ".lnk" : ".txt",
                FileCategory = i < 22 ? "Other" : "Documents", DirectoryCategory = i < 22 ? "AppData" : "Other",
                ScannerCategory = i < 22 ? "recent" : "filesystem", FileAgeDays = 150 + i, LogFileSize = 3.5f
            }).ToArray();
            var service = new PersonalAttentionModelService(root);
            var result = await service.TrainAsync(samples, CancellationToken.None);
            Assert.Equal(26, result.TrainedSamples);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PersonalScoreCanBeSorted()
    {
        var low = new Finding { Path = "low.txt", PersonalAttentionScore = 10 };
        var high = new Finding { Path = "high.txt", PersonalAttentionScore = 90 };
        Assert.Same(high, FindingPagination.Sort([low, high], nameof(Finding.PersonalAttentionScore), true).First());
    }

    [Fact]
    public void FindingIndexCollapsesDuplicateScannerEntriesByPath()
    {
        var first = new Finding { Path = @"C:\Users\Test\Recent\config.ini.lnk", ScannerId = "recent" };
        var duplicate = new Finding { Path = @"c:\users\test\recent\CONFIG.INI.LNK", ScannerId = "filesystem" };

        var index = PersonalAttentionFeatureExtractor.IndexFindingsByPath([first, duplicate]);

        Assert.Single(index);
        Assert.Same(first, index.Values.Single());
    }

    [Fact]
    public void ApplicationHistoryFeaturesUseLocalContext()
    {
        var entry = new ApplicationHistoryEntry(
            @"\\NAS\Finance\client.env", DateTime.Now.AddDays(-14), 27, true, false,
            @"C:\History\container.automaticDestinations-ms", "Automatic",
            HistoricalExposureScore: 75, ApplicationKey: "Microsoft Excel", ApplicationName: "Microsoft Excel");

        var features = PersonalAttentionFeatureExtractor.Extract(entry);

        Assert.Equal("ApplicationHistory", features.ItemSource);
        Assert.Equal("Microsoft Excel", features.ApplicationCategory);
        Assert.Equal("Automatic", features.HistorySourceKind);
        Assert.Equal(1, features.IsApplicationHistory);
        Assert.Equal(1, features.IsJumpList);
        Assert.Equal(1, features.HistoryPinned);
        Assert.Equal(1, features.HistoryNetworkPath);
        Assert.Equal(0, features.HistoryTargetExists);
        Assert.True(features.HistoryInteractionCount > 0);
        Assert.InRange(features.HistoryDaysSinceInteraction, 13, 15);
    }

    [Fact]
    public void ApplicationHistoryFeedbackIsScopedByApplicationAndPath()
    {
        var first = new ApplicationHistoryEntry(@"D:\Docs\contract.docx", null, 0, false, false, "one", "Automatic", ApplicationKey: "Word");
        var second = first with { ApplicationKey = "Explorer" };

        Assert.NotEqual(PersonalAttentionFeatureExtractor.ApplicationHistoryFeedbackKey(first),
            PersonalAttentionFeatureExtractor.ApplicationHistoryFeedbackKey(second));
    }

    [Fact]
    public void ApplicationPriorityUsesTopThreeObjectScores()
    {
        var entries = new[] { 90f, 80f, 70f, 10f }.Select((score, index) =>
        {
            var entry = new ApplicationHistoryEntry($@"D:\Docs\{index}.txt", null, 0, false, false, "container", "Automatic");
            entry.PersonalAttentionScore = score;
            return entry;
        }).ToArray();
        var application = new ApplicationHistoryApplication(new("app", "App", ApplicationIdentityConfidence.Known), entries, 1, 0);

        Assert.Equal(80f, application.PersonalAttentionScore);
    }

    [Fact]
    public void ApplicationHistoryFeedbackPersistsFeatureSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacy-audit-history-feedback-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            var db = new AuditDatabase(Path.Combine(root, "test.db"));
            var entry = new ApplicationHistoryEntry(@"D:\Docs\client.env", null, 3, true, false, "container", "Automatic", ApplicationKey: "Editor", ApplicationName: "Editor");
            var key = PersonalAttentionFeatureExtractor.ApplicationHistoryFeedbackKey(entry);
            db.SetPersonalFeedback("history:Editor", key, PersonalAttentionFeatureExtractor.Extract(entry), true);

            var stored = db.GetPersonalFeedback(PersonalAttentionSchema.Version).Single();
            var features = PersonalAttentionFeatureExtractor.Deserialize(stored.FeatureJson);
            Assert.Equal(key, stored.PathKey);
            Assert.True(stored.Label);
            Assert.NotNull(features);
            Assert.Equal("ApplicationHistory", features!.ItemSource);
            Assert.True(features.Label);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
