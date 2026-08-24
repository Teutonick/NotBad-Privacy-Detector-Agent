using System.Globalization;
using PrivacyAudit.Core;

namespace PrivacyAudit.Tests;

public sealed class CoreTests
{
    [Fact]
    public void ExposureScore_IsCappedAndExplainedByLevel()
    {
        Assert.Equal(100, ExposureCalculator.Calculate([90, 60]));
        Assert.Equal(RiskLevel.Critical, ExposureCalculator.Level(80));
        Assert.Equal(RiskLevel.High, ExposureCalculator.Level(60));
        Assert.Equal(RiskLevel.Medium, ExposureCalculator.Level(30));
        Assert.Equal(RiskLevel.Low, ExposureCalculator.Level(1));
        Assert.Equal(RiskLevel.None, ExposureCalculator.Level(0));
    }

    [Fact]
    public void ScanPresetPolicy_OnlyQuickScanOmitsSystemScanners()
    {
        Assert.False(ScanPresetPolicy.IncludesSystemScanners(ScanPreset.Quick));
        Assert.True(ScanPresetPolicy.IncludesSystemScanners(ScanPreset.Custom));
        Assert.True(ScanPresetPolicy.IncludesSystemScanners(ScanPreset.Full));
    }

    [Theory]
    [InlineData("photo.jpg", "Images")]
    [InlineData("movie.mkv", "Video")]
    [InlineData("voice.flac", "Audio")]
    [InlineData("backup.7z", "Archives")]
    [InlineData("report.pdf", "Documents")]
    [InlineData("model.gguf", "AI / Models")]
    [InlineData("unknown.xyz", "Other")]
    public void FileClassifier_UsesExtensionsCaseInsensitively(string path, string expected) => Assert.Equal(expected, Classifier.File(path.ToUpperInvariant()));

    [Fact]
    public void AgeClassifier_CoversOldAndRecentFiles()
    {
        Assert.Equal("< 6 months", Classifier.Age(DateTime.Now.AddDays(-2)));
        Assert.Equal("> 5 years", Classifier.Age(DateTime.Now.AddYears(-6)));
        Assert.Equal("Unknown", Classifier.Age(null));
    }

    [Fact]
    public void Localization_UsesRussianOnlyForRuAndEnglishFallbackOtherwise()
    {
        Assert.Equal("Обзор", LocalizationService.Get("NavOverview", new CultureInfo("ru-RU")));
        Assert.Equal("Overview", LocalizationService.Get("NavOverview", new CultureInfo("en-US")));
        Assert.Equal("Overview", LocalizationService.Get("NavOverview", new CultureInfo("de-DE")));
        Assert.Equal("Инспекция архивов", LocalizationService.Get("ArchivesFilter", new CultureInfo("ru-RU")));
        Assert.Equal("Archive inspection", LocalizationService.Get("ArchivesFilter", new CultureInfo("en-US")));
    }

    [Fact]
    public void Localization_LanguageButtonUsesCompactCodes()
    {
        Assert.Equal("RU", LocalizationService.GetLanguageCode(new CultureInfo("ru-RU")));
        Assert.Equal("EN", LocalizationService.GetLanguageCode(new CultureInfo("en-US")));
        Assert.NotEmpty(LocalizationService.Get("LanguageRestartPrompt", new CultureInfo("ru-RU")));
        Assert.NotEmpty(LocalizationService.Get("LanguageSwitchTooltip", new CultureInfo("en-US")));
    }

    [Fact]
    public void Localization_IncludesLegendAndTooltipKeys()
    {
        var ru = new CultureInfo("ru-RU");
        var en = new CultureInfo("en-US");

        Assert.Equal("Риск обнаружения", LocalizationService.Get("AllRisks", ru));
        Assert.Equal("Detection risk", LocalizationService.Get("AllRisks", en));

        Assert.Equal("Найти конфиги и доступы", LocalizationService.Get("SearchConfigs", ru));
        Assert.Equal("Find Credentials & Configs", LocalizationService.Get("SearchConfigs", en));

        Assert.Equal("Найти цифровой след", LocalizationService.Get("SearchIdentity", ru));
        Assert.Equal("Find Identity Traces", LocalizationService.Get("SearchIdentity", en));

        Assert.Contains("Легенда", LocalizationService.Get("LegendButton", ru));
        Assert.Contains("Legend", LocalizationService.Get("LegendButton", en));

        Assert.NotEmpty(LocalizationService.Get("RiskFilterHelp", ru));
        Assert.Contains("Recent", LocalizationService.Get("RiskFilterHelp", ru));
        Assert.Contains("Recent", LocalizationService.Get("RiskFilterHelp", en));

        Assert.Contains("Медиа", LocalizationService.Get("MediaLegendTitle", ru));
        Assert.Contains("Media", LocalizationService.Get("MediaLegendTitle", en));
        Assert.NotEmpty(LocalizationService.Get("MediaPeopleFilterHelp", ru));
        Assert.NotEmpty(LocalizationService.Get("DocumentScanHelp", ru));
        Assert.NotEmpty(LocalizationService.Get("ExifScanHelp", ru));
        Assert.NotEmpty(LocalizationService.Get("PeopleScanHelp", ru));
    }

    [Fact]
    public void Localization_RussianTone_UsesInformalStyle()
    {
        var ru = new CultureInfo("ru-RU");
        Assert.Equal("Привет", LocalizationService.Get("Greeting", ru));
        Assert.Contains("за тобой", LocalizationService.Get("SafeAuditDescription", ru));
        Assert.Contains("твоей", LocalizationService.Get("IdentityScanning", ru));
        Assert.Contains("твои", LocalizationService.Get("PersonalModelNotTrained", ru));
    }

    [Fact]
    public void PersonalModel_UserExplanationTracksCurrentTrainingPolicy()
    {
        var ru = LocalizationService.Get("PersonalModelInfoText", new CultureInfo("ru-RU"));
        var en = LocalizationService.Get("PersonalModelInfoText", new CultureInfo("en-US"));

        foreach (var text in new[] { ru, en })
        {
            Assert.Contains(PersonalAttentionSchema.MinimumSamples.ToString(CultureInfo.InvariantCulture), text);
            Assert.Contains(PersonalAttentionSchema.MinimumPerClass.ToString(CultureInfo.InvariantCulture), text);
            Assert.Contains(PersonalAttentionSchema.RetrainInterval.ToString(CultureInfo.InvariantCulture), text);
            Assert.Contains(((int)(PersonalAttentionSchema.MinimumMinorityFraction * 100)).ToString(CultureInfo.InvariantCulture) + "%", text);
            Assert.Contains("GPS", text);
            Assert.Contains("EXIF", text);
            Assert.Contains("100", text);
        }
    }
}
