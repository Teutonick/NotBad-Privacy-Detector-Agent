using PrivacyAudit.Core;

namespace PrivacyAudit.PeopleDetection;

public sealed class ImageSafetyScanner(ModelManager modelManager, ImageSafetyRepository repository)
{
    public async Task<IReadOnlyList<ImageSafetyScanResult>> ScanAsync(IEnumerable<Finding> findings, bool forceRescan = false, IProgress<ImageSafetyScanProgress>? progress = null, CancellationToken token = default)
    {
        var files = findings.Where(x => x.Category == "Images").ToArray();
        if (!await modelManager.IsInstalledAsync(token)) throw new ModelDownloadException("Image Safety model is not installed.", "model_missing");
        var results = new List<ImageSafetyScanResult>(files.Length); var nsfw = 0; var nsfl = 0; var errors = 0;
        using var classifier = new ImageSafetyClassifier(modelManager.ModelPath);
        foreach (var finding in files)
        {
            token.ThrowIfCancellationRequested(); var file = new FileInfo(finding.Path); ImageSafetyScanResult result;
            if (!file.Exists) result = Error(finding.Path, finding.SizeBytes, finding.ModifiedAt ?? DateTime.MinValue, modelManager.Manifest.ModelVersion, "File is no longer available.");
            else
            {
                var cached = forceRescan ? null : await Task.Run(() => repository.FindReusable(finding.Path, file.Length, file.LastWriteTime, modelManager.Manifest.ModelVersion), token);
                if (cached is not null) result = cached;
                else try
                {
                    var score = await Task.Run(() => classifier.Classify(finding.Path, token), token);
                    result = new(finding.Path, ImageSafetyScanStatus.Completed, score.PrimaryClass, score.Nsfl, score.Nsfw, score.Sfw, modelManager.Manifest.ModelVersion, DateTime.UtcNow, file.Length, file.LastWriteTime);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { modelManager.LogScanError("Image Safety", finding.Path, ex); result = Error(finding.Path, file.Length, file.LastWriteTime, modelManager.Manifest.ModelVersion, ex.Message); }
            }
            await Task.Run(() => repository.Upsert(result), token); results.Add(result);
            if (result.PrimaryClass == ImageSafetyClass.NSFW && result.Status == ImageSafetyScanStatus.Completed) nsfw++;
            if (result.PrimaryClass == ImageSafetyClass.NSFL && result.Status == ImageSafetyScanStatus.Completed) nsfl++;
            if (result.Status == ImageSafetyScanStatus.Error) errors++;
            progress?.Report(new(finding.Path, results.Count, files.Length, nsfw, nsfl, errors, result.Error));
        }
        return results;
    }
    static ImageSafetyScanResult Error(string path, long size, DateTime modified, string version, string error) => new(path, ImageSafetyScanStatus.Error, ImageSafetyClass.SFW, 0, 0, 0, version, DateTime.UtcNow, size, modified, error);
}
