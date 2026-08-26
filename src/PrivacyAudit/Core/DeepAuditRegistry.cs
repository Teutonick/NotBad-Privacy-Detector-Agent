using PrivacyAudit.PeopleDetection;

namespace PrivacyAudit.Core;

public sealed record DeepScannerProgress(string ScannerId, int Completed, int Total, int Confirmed, int Errors, string CurrentPath);
public sealed record DeepScannerBatchResult(string ScannerId, int Completed, int Confirmed, int Errors, string? UnavailableReason = null, IReadOnlyList<Guid>? FailedFindingIds = null);

public interface IDeepAuditScanner
{
    string Id { get; }
    string NameKey { get; }
    Task<bool> IsAvailableAsync(CancellationToken token = default);
    Task<DeepScannerBatchResult> AnalyzeAsync(IReadOnlyList<Finding> findings, IProgress<DeepScannerProgress>? progress, CancellationToken token);
}

public sealed class DeepAuditScannerRegistry
{
    readonly IReadOnlyDictionary<string, IDeepAuditScanner> _scanners;
    public DeepAuditScannerRegistry(IEnumerable<IDeepAuditScanner> scanners) =>
        _scanners = scanners.ToDictionary(scanner => scanner.Id, StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<IDeepAuditScanner> Scanners => _scanners.Values.ToArray();
    public bool TryGet(string id, out IDeepAuditScanner scanner) => _scanners.TryGetValue(id, out scanner!);

    public static DeepAuditScannerRegistry CreateDefault(ModelManager modelManager, PeopleScanRepository peopleRepository, ModelManager? imageSafetyModelManager = null, ImageSafetyRepository? imageSafetyRepository = null) => new(new IDeepAuditScanner[]
    {
        new PiiDeepScanner(), new SecretsDeepScanner(), new ConfigDeepScanner(), new IdentityDeepScanner(),
        new ArchiveDeepScanner(), new DocumentDeepScanner(), new ExifDeepScanner(), new PeopleDeepScanner(modelManager, peopleRepository)
    }.Concat(imageSafetyModelManager is not null && imageSafetyRepository is not null
        ? [new ImageSafetyDeepScanner(imageSafetyModelManager, imageSafetyRepository)]
        : []));
}

abstract class DeepAuditScannerBase(string id, string nameKey) : IDeepAuditScanner
{
    public string Id => id;
    public string NameKey => nameKey;
    public virtual Task<bool> IsAvailableAsync(CancellationToken token = default) => Task.FromResult(true);

    public async Task<DeepScannerBatchResult> AnalyzeAsync(IReadOnlyList<Finding> findings, IProgress<DeepScannerProgress>? progress, CancellationToken token)
    {
        var confirmed = 0;
        var errors = 0;
        var failed = new List<Guid>();
        for (var index = 0; index < findings.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var finding = findings[index];
            try
            {
                if (await AnalyzeFindingAsync(finding, token)) confirmed++;
                finding.MetadataJson = DetectionEvidenceCalculator.MarkCompleted(finding.MetadataJson, Id);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors++;
                failed.Add(finding.Id);
                CrashLogger.LogException(ex, $"Priority deep scanner {Id}: {finding.Path}");
            }
            progress?.Report(new(Id, index + 1, findings.Count, confirmed, errors, finding.Path));
        }
        return new(Id, findings.Count, confirmed, errors, FailedFindingIds: failed);
    }

    protected abstract Task<bool> AnalyzeFindingAsync(Finding finding, CancellationToken token);
}

sealed class PiiDeepScanner() : DeepAuditScannerBase(DetectionEvidenceCalculator.Pii, "SearchPii")
{
    protected override Task<bool> AnalyzeFindingAsync(Finding finding, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var text = TextExtractor.ExtractText(finding.Path);
        var result = string.IsNullOrWhiteSpace(text) ? new PiiDetectionResult() : PiiDetector.Scan(text);
        finding.MetadataJson = PiiDetectionResult.InjectIntoMetadata(finding.MetadataJson, result);
        return result.TotalMatches > 0;
    }, token);
}

sealed class SecretsDeepScanner() : DeepAuditScannerBase(DetectionEvidenceCalculator.Secrets, "SearchSecrets")
{
    protected override Task<bool> AnalyzeFindingAsync(Finding finding, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var text = TextExtractor.ExtractText(finding.Path);
        var result = string.IsNullOrWhiteSpace(text) ? new SecretDetectionResult() : SecretDetector.Scan(text, finding.Path);
        finding.MetadataJson = SecretDetectionResult.InjectIntoMetadata(finding.MetadataJson, result);
        return result.TotalMatches > 0;
    }, token);
}

sealed class ConfigDeepScanner() : DeepAuditScannerBase(DetectionEvidenceCalculator.Configs, "SearchConfigs")
{
    protected override Task<bool> AnalyzeFindingAsync(Finding finding, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var result = CredentialConfigDetector.Analyze(finding.Path);
        finding.MetadataJson = CredentialConfigResult.InjectIntoMetadata(finding.MetadataJson, result);
        return result.IsCredentialConfig;
    }, token);
}

sealed class IdentityDeepScanner : DeepAuditScannerBase
{
    UserIdentityProfile? _profile;
    public IdentityDeepScanner() : base(DetectionEvidenceCalculator.Identity, "SearchIdentity") { }
    protected override Task<bool> AnalyzeFindingAsync(Finding finding, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        _profile ??= UserIdentityProfile.Collect();
        var result = IdentityTraceDetector.Analyze(finding.Path, _profile);
        finding.MetadataJson = IdentityTraceResult.InjectIntoMetadata(finding.MetadataJson, result);
        return result.HasIdentityTrace;
    }, token);
}

sealed class ArchiveDeepScanner() : DeepAuditScannerBase(DetectionEvidenceCalculator.Archives, "SearchArchives")
{
    protected override Task<bool> AnalyzeFindingAsync(Finding finding, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var result = ArchiveInspector.Inspect(finding.Path);
        finding.MetadataJson = ArchiveInspectionResult.InjectIntoMetadata(finding.MetadataJson, result);
        return result.SensitiveEntriesCount > 0;
    }, token);
}

sealed class DocumentDeepScanner() : DeepAuditScannerBase(DetectionEvidenceCalculator.Documents, "SearchDocuments")
{
    protected override Task<bool> AnalyzeFindingAsync(Finding finding, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var result = DocumentDetector.Analyze(finding.Path);
        finding.MetadataJson = DocumentDetectionResult.InjectIntoMetadata(finding.MetadataJson, result);
        return result.IsDocument;
    }, token);
}

sealed class ExifDeepScanner() : DeepAuditScannerBase(DetectionEvidenceCalculator.Exif, "SearchExifGeo")
{
    protected override Task<bool> AnalyzeFindingAsync(Finding finding, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var result = ExifMetadataExtractor.Extract(finding.Path);
        finding.MetadataJson = ExifMetadataResult.InjectIntoMetadata(finding.MetadataJson, result);
        return result.DisclosedFields.Count > 0;
    }, token);
}

sealed class PeopleDeepScanner(ModelManager modelManager, PeopleScanRepository repository) : IDeepAuditScanner
{
    public string Id => DetectionEvidenceCalculator.People;
    public string NameKey => "SearchPeople";
    public Task<bool> IsAvailableAsync(CancellationToken token = default) => modelManager.IsInstalledAsync(token);

    public async Task<DeepScannerBatchResult> AnalyzeAsync(IReadOnlyList<Finding> findings, IProgress<DeepScannerProgress>? progress, CancellationToken token)
    {
        if (!await IsAvailableAsync(token)) return new(Id, 0, 0, 0, "YuNet model is not installed");
        var scanner = new PeopleScanner(modelManager, repository);
        var adapter = new Progress<PeopleScanProgress>(value => progress?.Report(new(Id, value.Completed, value.Total, value.People, value.Errors, value.CurrentPath)));
        var results = await scanner.ScanAsync(findings, adapter, token);
        var byPath = findings.ToDictionary(finding => finding.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            if (!byPath.TryGetValue(result.Path, out var finding)) continue;
            finding.MetadataJson = PeopleScanMetadata.InjectIntoMetadata(finding.MetadataJson, result);
            if (result.Status == PeopleScanStatus.Completed)
                finding.MetadataJson = DetectionEvidenceCalculator.MarkCompleted(finding.MetadataJson, Id);
        }
        var failed = results.Where(result => result.Status == PeopleScanStatus.Error).Select(result => byPath.GetValueOrDefault(result.Path)?.Id).Where(id => id is not null).Select(id => id!.Value).ToArray();
        return new(Id, results.Count, results.Count(result => result.PeopleDetected), failed.Length, FailedFindingIds: failed);
    }
}

sealed class ImageSafetyDeepScanner(ModelManager modelManager, ImageSafetyRepository repository) : IDeepAuditScanner
{
    public string Id => DetectionEvidenceCalculator.ImageSafety;
    public string NameKey => "SearchNsfw";
    public Task<bool> IsAvailableAsync(CancellationToken token = default) => modelManager.IsInstalledAsync(token);
    public async Task<DeepScannerBatchResult> AnalyzeAsync(IReadOnlyList<Finding> findings, IProgress<DeepScannerProgress>? progress, CancellationToken token)
    {
        if (!await IsAvailableAsync(token)) return new(Id, 0, 0, 0, "Image Safety model is not installed");
        var adapter = new Progress<ImageSafetyScanProgress>(x => progress?.Report(new(Id, x.Completed, x.Total, x.Nsfw + x.Nsfl, x.Errors, x.CurrentPath)));
        var results = await new ImageSafetyScanner(modelManager, repository).ScanAsync(findings, false, adapter, token);
        var byPath = findings.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var result in results.Where(x => x.Status == ImageSafetyScanStatus.Completed))
            if (byPath.TryGetValue(result.Path, out var finding))
            { finding.MetadataJson = ImageSafetyMetadata.InjectIntoMetadata(finding.MetadataJson, result); finding.MetadataJson = DetectionEvidenceCalculator.MarkCompleted(finding.MetadataJson, Id); }
        var failed = results.Where(x => x.Status == ImageSafetyScanStatus.Error).Select(x => byPath.GetValueOrDefault(x.Path)?.Id).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        return new(Id, results.Count, results.Count(x => x.Status == ImageSafetyScanStatus.Completed && x.PrimaryClass != ImageSafetyClass.SFW), failed.Length, FailedFindingIds: failed);
    }
}
