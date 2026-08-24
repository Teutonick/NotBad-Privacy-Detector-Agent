using PrivacyAudit.Core;

namespace PrivacyAudit.PeopleDetection;

public sealed class PeopleScanner(ModelManager modelManager, PeopleScanRepository repository)
{
    public async Task<IReadOnlyList<PeopleScanResult>> ScanAsync(IEnumerable<Finding> imageFindings, IProgress<PeopleScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var files = imageFindings.Where(x => x.Category == "Images").ToArray();
        var modelPath = await modelManager.IsInstalledAsync(cancellationToken) ? modelManager.ModelPath : throw new InvalidOperationException("The YuNet model is not installed or failed integrity verification.");
        var results = new List<PeopleScanResult>(files.Length);
        var completed = 0;
        var people = 0;
        var errors = 0;
        using var detector = new YuNetDetector(modelPath);
        foreach (var finding in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(finding.Path);
            if (!file.Exists)
            {
                var missing = Error(finding.Path, finding.SizeBytes, finding.ModifiedAt ?? DateTime.MinValue, modelManager.Manifest.ModelVersion, "File is no longer available.");
                await Task.Run(() => repository.Upsert(missing), cancellationToken); results.Add(missing); errors++; completed++;
                progress?.Report(new(finding.Path, completed, files.Length, people, errors, missing.Error));
                continue;
            }

            var cached = await Task.Run(() => repository.FindReusable(finding.Path, file.Length, file.LastWriteTime, modelManager.Manifest.ModelVersion), cancellationToken);
            if (cached is not null)
            {
                results.Add(cached); completed++; if (cached.PeopleDetected) people++; if (cached.Status == PeopleScanStatus.Error) errors++;
                progress?.Report(new(finding.Path, completed, files.Length, people, errors, "Cached"));
                continue;
            }

            PeopleScanResult result;
            try
            {
                var detection = await Task.Run(() => detector.Detect(finding.Path, cancellationToken), cancellationToken);
                result = new(finding.Path, PeopleScanStatus.Completed, detection.FaceCount > 0, detection.FaceCount, detection.MaxConfidence, modelManager.Manifest.ModelVersion, DateTime.UtcNow, file.Length, file.LastWriteTime);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                modelManager.LogPeopleScanError(finding.Path, ex);
                var message = ex is SixLabors.ImageSharp.UnknownImageFormatException or SixLabors.ImageSharp.InvalidImageContentException or ArgumentException
                    ? "The image could not be decoded. The format may be unsupported or the file may be damaged."
                    : ex.Message;
                result = Error(finding.Path, file.Length, file.LastWriteTime, modelManager.Manifest.ModelVersion, message);
            }
            await Task.Run(() => repository.Upsert(result), cancellationToken); results.Add(result); completed++; if (result.PeopleDetected) people++; if (result.Status == PeopleScanStatus.Error) errors++;
            progress?.Report(new(finding.Path, completed, files.Length, people, errors, result.Error));
        }
        return results;
    }

    static PeopleScanResult Error(string path, long size, DateTime modified, string modelVersion, string error) => new(path, PeopleScanStatus.Error, false, 0, 0, modelVersion, DateTime.UtcNow, size, modified, error);
}
