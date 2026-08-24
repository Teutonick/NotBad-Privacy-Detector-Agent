using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.ML;
using Microsoft.ML.Data;
using PrivacyAudit.PeopleDetection;

namespace PrivacyAudit.Core;

public static class PersonalAttentionSchema
{
    public const int Version = 2;
    public const int MinimumSamples = 20;
    public const int MinimumPerClass = 5;
    public const int RetrainInterval = 10;
    public const double MinimumMinorityFraction = 0.20;
    public const int MaxFeedbackRows = StorageLimits.MaxPersonalFeedbackRows;
}

public sealed record PersonalFeedbackRecord(string FindingId, string PathKey, bool Label, DateTime CreatedAt,
    DateTime UpdatedAt, int FeatureSchemaVersion, string FeatureJson);

public sealed record PersonalModelStats(int Total, int Positive, int Negative, int TrainedSamples = 0)
{
    public bool CanTrain => Total >= PersonalAttentionSchema.MinimumSamples &&
        Positive >= PersonalAttentionSchema.MinimumPerClass && Negative >= PersonalAttentionSchema.MinimumPerClass &&
        Math.Min(Positive, Negative) / (double)Math.Max(1, Total) >= PersonalAttentionSchema.MinimumMinorityFraction;
}

public sealed class PersonalAttentionFeatures
{
    public bool Label { get; set; }
    public float ExposureScore { get; set; }
    public float LogFileSize { get; set; }
    public float FileAgeDays { get; set; }
    public float PersonalDataMatches { get; set; }
    public float SecretMatches { get; set; }
    public float IdentityMentions { get; set; }
    public float ArchiveSensitiveEntries { get; set; }
    public float DocumentConfidence { get; set; }
    public float FaceCount { get; set; }
    public float ExifFieldCount { get; set; }
    public float HistoryInteractionCount { get; set; }
    public float HistoryDaysSinceInteraction { get; set; }
    public float IsRecent { get; set; }
    public float IsJumpList { get; set; }
    public float IsSearchIndexed { get; set; }
    public float CredentialConfigDetected { get; set; }
    public float IdentityTraceDetected { get; set; }
    public float IsArchive { get; set; }
    public float DocumentLike { get; set; }
    public float PeopleDetected { get; set; }
    public float HasGps { get; set; }
    public float HasExif { get; set; }
    public float HasCameraSerial { get; set; }
    public float EntropySecretDetected { get; set; }
    public float ScreenshotLike { get; set; }
    public float IsApplicationHistory { get; set; }
    public float HistoryTargetExists { get; set; }
    public float HistoryPinned { get; set; }
    public float HistoryNetworkPath { get; set; }
    public float HistoryHasAuditFinding { get; set; }
    public string Extension { get; set; } = "(none)";
    public string FileCategory { get; set; } = "Other";
    public string DirectoryCategory { get; set; } = "Other";
    public string ScannerCategory { get; set; } = "Other";
    public string ItemSource { get; set; } = "Finding";
    public string ApplicationCategory { get; set; } = "(none)";
    public string HistorySourceKind { get; set; } = "(none)";
}

public sealed class PersonalAttentionPrediction
{
    [ColumnName("PredictedLabel")] public bool PredictedLabel { get; set; }
    public float Probability { get; set; }
    public float Score { get; set; }
}

public static class PersonalAttentionFeatureExtractor
{
    public static PersonalAttentionFeatures Extract(Finding finding, bool label = false)
    {
        PiiDetectionResult.TryParse(finding.MetadataJson, out var pii);
        SecretDetectionResult.TryParse(finding.MetadataJson, out var secrets);
        CredentialConfigResult.TryParse(finding.MetadataJson, out var config);
        IdentityTraceResult.TryParse(finding.MetadataJson, out var identity);
        ArchiveInspectionResult.TryParse(finding.MetadataJson, out var archive);
        DocumentDetectionResult.TryParse(finding.MetadataJson, out var document);
        PeopleScanMetadata.TryParse(finding.MetadataJson, out var people);
        ExifMetadataResult.TryParse(finding.MetadataJson, out var exif);
        var age = finding.ModifiedAt is DateTime modified ? Math.Max(0, (DateTime.Now - modified).TotalDays) : 0;
        return new()
        {
            Label = label, ExposureScore = finding.ExposureScore, LogFileSize = (float)Math.Log10(Math.Max(1, finding.SizeBytes)),
            FileAgeDays = (float)Math.Min(age, 36500), PersonalDataMatches = pii?.TotalMatches ?? 0,
            SecretMatches = secrets?.TotalMatches ?? 0, IdentityMentions = identity?.TotalMentions ?? 0,
            ArchiveSensitiveEntries = archive?.SensitiveEntriesCount ?? 0, DocumentConfidence = (float)(document?.Confidence ?? 0),
            FaceCount = people?.FaceCount ?? 0, ExifFieldCount = exif?.DisclosedFields.Count ?? 0,
            IsRecent = finding.Category.Equals("Recent", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            IsJumpList = finding.Category.Equals("Jump Lists", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            IsSearchIndexed = finding.ScannerId.Contains("search", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            CredentialConfigDetected = config?.IsCredentialConfig == true ? 1 : 0, IdentityTraceDetected = identity?.HasIdentityTrace == true ? 1 : 0,
            IsArchive = archive?.IsArchive == true ? 1 : 0, DocumentLike = document?.IsDocument == true ? 1 : 0,
            PeopleDetected = people?.PeopleDetected == true ? 1 : 0, HasGps = exif?.HasGeolocation == true ? 1 : 0,
            HasExif = exif?.DisclosedFields.Count > 0 ? 1 : 0, HasCameraSerial = !string.IsNullOrWhiteSpace(exif?.CameraSerialNumber) ? 1 : 0,
            EntropySecretDetected = secrets?.Categories.Any(x => x.Contains("entropy", StringComparison.OrdinalIgnoreCase)) == true ? 1 : 0,
            ScreenshotLike = finding.DisplayName.Contains("screenshot", StringComparison.OrdinalIgnoreCase) || finding.DisplayName.Contains("скриншот", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            Extension = Path.GetExtension(finding.Path).ToLowerInvariant() is { Length: > 0 } ext ? ext : "(none)",
            FileCategory = Classifier.File(finding.Path), DirectoryCategory = DirectoryKind(finding.Path),
            ScannerCategory = finding.ScannerId
        };
    }

    public static PersonalAttentionFeatures Extract(ApplicationHistoryEntry entry, Finding? relatedFinding = null, bool label = false)
    {
        var source = relatedFinding ?? new Finding
        {
            ScannerId = "application-history", Category = entry.IsDirectory ? "Directory" : Classifier.File(entry.TargetPath),
            Path = entry.TargetPath, DisplayName = Path.GetFileName(entry.TargetPath.TrimEnd('\\', '/')),
            IsDirectory = entry.IsDirectory, SizeBytes = entry.SizeBytes,
            ModifiedAt = entry.TargetModifiedAt ?? entry.LastInteraction, ExposureScore = entry.HistoricalExposureScore
        };
        var features = Extract(source, label);
        var interactionAge = entry.LastInteraction is DateTime last ? Math.Max(0, (DateTime.Now - last).TotalDays) : 0;
        features.ItemSource = "ApplicationHistory";
        features.ApplicationCategory = string.IsNullOrWhiteSpace(entry.ApplicationName) ? "Unknown application" : entry.ApplicationName;
        features.HistorySourceKind = string.IsNullOrWhiteSpace(entry.SourceKind) ? "Unknown" : entry.SourceKind;
        features.IsApplicationHistory = 1;
        features.IsJumpList = 1;
        features.HistoryTargetExists = entry.ExistsNow ? 1 : 0;
        features.HistoryPinned = entry.IsPinned ? 1 : 0;
        features.HistoryNetworkPath = entry.TargetPath.StartsWith("\\\\", StringComparison.Ordinal) ? 1 : 0;
        features.HistoryHasAuditFinding = entry.RelatedFindingId is not null ? 1 : 0;
        features.HistoryInteractionCount = (float)Math.Log10(Math.Max(1, entry.InteractionCount + 1));
        features.HistoryDaysSinceInteraction = (float)Math.Min(interactionAge, 36500);
        if (entry.SizeBytes > 0) features.LogFileSize = (float)Math.Log10(Math.Max(1, entry.SizeBytes));
        if (entry.TargetModifiedAt is DateTime modified) features.FileAgeDays = (float)Math.Min(Math.Max(0, (DateTime.Now - modified).TotalDays), 36500);
        return features;
    }

    public static string Serialize(PersonalAttentionFeatures value) => JsonSerializer.Serialize(value);
    public static PersonalAttentionFeatures? Deserialize(string json) { try { return JsonSerializer.Deserialize<PersonalAttentionFeatures>(json); } catch { return null; } }
    public static string PathKey(string path)
    {
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant(); }
        catch { return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant(); }
    }
    public static string ApplicationHistoryFeedbackKey(ApplicationHistoryEntry entry) =>
        $"APPLICATION-HISTORY|{entry.ApplicationKey.ToUpperInvariant()}|{PathKey(entry.TargetPath)}";
    public static Dictionary<string, Finding> IndexFindingsByPath(IEnumerable<Finding> findings) =>
        findings.GroupBy(x => PathKey(x.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Explain(Finding finding)
    {
        var f = Extract(finding);
        var factors = new List<string>();
        if (f.PersonalDataMatches > 0) factors.Add(LocalizationService.Get("PersonalFactorPii"));
        if (f.SecretMatches > 0 || f.CredentialConfigDetected > 0) factors.Add(LocalizationService.Get("PersonalFactorSecrets"));
        if (f.FileAgeDays >= 730) factors.Add(LocalizationService.Get("PersonalFactorOld"));
        if (f.HasGps > 0 || f.HasCameraSerial > 0) factors.Add(LocalizationService.Get("PersonalFactorMetadata"));
        if (f.DocumentLike > 0) factors.Add(LocalizationService.Get("PersonalFactorDocument"));
        if (f.PeopleDetected > 0) factors.Add(LocalizationService.Get("PersonalFactorPeople"));
        return factors.Take(3).ToArray();
    }

    static string DirectoryKind(string path)
    {
        var p = path.Replace('/', '\\');
        if (p.Contains("\\Downloads\\", StringComparison.OrdinalIgnoreCase)) return "Downloads";
        if (p.Contains("\\Desktop\\", StringComparison.OrdinalIgnoreCase)) return "Desktop";
        if (p.Contains("\\Documents\\", StringComparison.OrdinalIgnoreCase)) return "Documents";
        if (p.Contains("\\Pictures\\", StringComparison.OrdinalIgnoreCase)) return "Pictures";
        if (p.Contains("\\AppData\\", StringComparison.OrdinalIgnoreCase)) return "AppData";
        if (p.Contains("\\Temp\\", StringComparison.OrdinalIgnoreCase)) return "Temp";
        return "Other";
    }
}

public sealed record PersonalModelMetadata(
    [property: JsonPropertyName("model_type")] string ModelType,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("positive_samples")] int PositiveSamples,
    [property: JsonPropertyName("negative_samples")] int NegativeSamples,
    [property: JsonPropertyName("feature_schema_version")] int FeatureSchemaVersion,
    [property: JsonPropertyName("trained_samples")] int TrainedSamples);

public sealed class PersonalAttentionModelService
{
    static readonly string[] Numeric = [nameof(PersonalAttentionFeatures.ExposureScore), nameof(PersonalAttentionFeatures.LogFileSize), nameof(PersonalAttentionFeatures.FileAgeDays), nameof(PersonalAttentionFeatures.PersonalDataMatches), nameof(PersonalAttentionFeatures.SecretMatches), nameof(PersonalAttentionFeatures.IdentityMentions), nameof(PersonalAttentionFeatures.ArchiveSensitiveEntries), nameof(PersonalAttentionFeatures.DocumentConfidence), nameof(PersonalAttentionFeatures.FaceCount), nameof(PersonalAttentionFeatures.ExifFieldCount), nameof(PersonalAttentionFeatures.HistoryInteractionCount), nameof(PersonalAttentionFeatures.HistoryDaysSinceInteraction)];
    static readonly string[] Boolean = [nameof(PersonalAttentionFeatures.IsRecent), nameof(PersonalAttentionFeatures.IsJumpList), nameof(PersonalAttentionFeatures.IsSearchIndexed), nameof(PersonalAttentionFeatures.CredentialConfigDetected), nameof(PersonalAttentionFeatures.IdentityTraceDetected), nameof(PersonalAttentionFeatures.IsArchive), nameof(PersonalAttentionFeatures.DocumentLike), nameof(PersonalAttentionFeatures.PeopleDetected), nameof(PersonalAttentionFeatures.HasGps), nameof(PersonalAttentionFeatures.HasExif), nameof(PersonalAttentionFeatures.HasCameraSerial), nameof(PersonalAttentionFeatures.EntropySecretDetected), nameof(PersonalAttentionFeatures.ScreenshotLike), nameof(PersonalAttentionFeatures.IsApplicationHistory), nameof(PersonalAttentionFeatures.HistoryTargetExists), nameof(PersonalAttentionFeatures.HistoryPinned), nameof(PersonalAttentionFeatures.HistoryNetworkPath), nameof(PersonalAttentionFeatures.HistoryHasAuditFinding)];
    readonly MLContext _ml = new(seed: 1701);
    readonly string _directory;
    string ModelPath => Path.Combine(_directory, "attention-model.zip");
    string MetadataPath => Path.Combine(_directory, "metadata.json");
    ITransformer? _model;

    public PersonalAttentionModelService(string localAppDataRoot) { _directory = Path.Combine(localAppDataRoot, "Models", "Personal"); TryLoad(); }
    public bool IsReady => _model is not null;
    public PersonalModelMetadata? Metadata { get; private set; }

    public bool TryLoad()
    {
        _model = null; Metadata = null;
        try
        {
            if (!File.Exists(ModelPath) || !File.Exists(MetadataPath)) return false;
            var metadata = JsonSerializer.Deserialize<PersonalModelMetadata>(File.ReadAllText(MetadataPath));
            if (metadata?.FeatureSchemaVersion != PersonalAttentionSchema.Version) return false;
            _model = _ml.Model.Load(ModelPath, out _); Metadata = metadata; return true;
        }
        catch { _model = null; Metadata = null; return false; }
    }

    public async Task<PersonalModelMetadata> TrainAsync(IReadOnlyList<PersonalAttentionFeatures> samples, CancellationToken token)
    {
        var positive = samples.Count(x => x.Label); var stats = new PersonalModelStats(samples.Count, positive, samples.Count - positive);
        if (!stats.CanTrain) throw new InvalidOperationException("Training sample requirements are not met.");
        token.ThrowIfCancellationRequested();
        var trained = await Task.Run(() =>
        {
            var data = _ml.Data.LoadFromEnumerable(samples);
            var categorical = _ml.Transforms.Categorical.OneHotEncoding("ExtensionEncoded", nameof(PersonalAttentionFeatures.Extension))
                .Append(_ml.Transforms.Categorical.OneHotEncoding("FileCategoryEncoded", nameof(PersonalAttentionFeatures.FileCategory)))
                .Append(_ml.Transforms.Categorical.OneHotEncoding("DirectoryCategoryEncoded", nameof(PersonalAttentionFeatures.DirectoryCategory)))
                .Append(_ml.Transforms.Categorical.OneHotEncoding("ScannerCategoryEncoded", nameof(PersonalAttentionFeatures.ScannerCategory)))
                .Append(_ml.Transforms.Categorical.OneHotEncoding("ItemSourceEncoded", nameof(PersonalAttentionFeatures.ItemSource)))
                .Append(_ml.Transforms.Categorical.OneHotEncoding("ApplicationCategoryEncoded", nameof(PersonalAttentionFeatures.ApplicationCategory)))
                .Append(_ml.Transforms.Categorical.OneHotEncoding("HistorySourceKindEncoded", nameof(PersonalAttentionFeatures.HistorySourceKind)));
            var columns = Numeric.Concat(Boolean).Concat(["ExtensionEncoded", "FileCategoryEncoded", "DirectoryCategoryEncoded", "ScannerCategoryEncoded", "ItemSourceEncoded", "ApplicationCategoryEncoded", "HistorySourceKindEncoded"]).ToArray();
            var pipeline = categorical.Append(_ml.Transforms.Concatenate("Features", columns))
                .Append(_ml.Transforms.NormalizeMeanVariance("Features"))
                .Append(_ml.BinaryClassification.Trainers.SdcaLogisticRegression(new Microsoft.ML.Trainers.SdcaLogisticRegressionBinaryTrainer.Options
                {
                    MaximumNumberOfIterations = 30,
                    ConvergenceTolerance = 1e-4f,
                    L1Regularization = 0.001f,
                    L2Regularization = 0.01f,
                    Shuffle = false
                }));
            return pipeline.Fit(data);
        }, token);
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_directory);
        var temporary = ModelPath + ".tmp";
        _ml.Model.Save(trained, null, temporary);
        File.Move(temporary, ModelPath, true);
        var metadata = new PersonalModelMetadata("SdcaLogisticRegression", DateTime.UtcNow, positive, samples.Count - positive, PersonalAttentionSchema.Version, samples.Count);
        File.WriteAllText(MetadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        _model = trained; Metadata = metadata; return metadata;
    }

    public float? Predict(Finding finding)
    {
        if (_model is null) return null;
        var engine = _ml.Model.CreatePredictionEngine<PersonalAttentionFeatures, PersonalAttentionPrediction>(_model);
        return Math.Clamp(engine.Predict(PersonalAttentionFeatureExtractor.Extract(finding)).Probability * 100, 0, 100);
    }

    /// <summary>Scores a batch with one prediction engine. Creating an engine per row is very expensive.</summary>
    public IReadOnlyList<float?> PredictMany(IReadOnlyList<Finding> findings, CancellationToken token = default)
        => PredictMany(findings.Select(x => PersonalAttentionFeatureExtractor.Extract(x)).ToArray(), token);

    public IReadOnlyList<float?> PredictMany(IReadOnlyList<PersonalAttentionFeatures> features, CancellationToken token = default)
    {
        if (_model is null) return Enumerable.Repeat<float?>(null, features.Count).ToArray();
        var engine = _ml.Model.CreatePredictionEngine<PersonalAttentionFeatures, PersonalAttentionPrediction>(_model);
        var scores = new float?[features.Count];
        for (var i = 0; i < features.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            scores[i] = Math.Clamp(engine.Predict(features[i]).Probability * 100, 0, 100);
        }
        return scores;
    }

    public Task<IReadOnlyList<float?>> PredictManyAsync(IReadOnlyList<Finding> findings, CancellationToken token = default) =>
        Task.Run(() => PredictMany(findings, token), token);

    public Task<IReadOnlyList<float?>> PredictManyAsync(IReadOnlyList<PersonalAttentionFeatures> features, CancellationToken token = default) =>
        Task.Run(() => PredictMany(features, token), token);

    public void DeleteModel() { _model = null; Metadata = null; if (File.Exists(ModelPath)) File.Delete(ModelPath); if (File.Exists(MetadataPath)) File.Delete(MetadataPath); }
}
