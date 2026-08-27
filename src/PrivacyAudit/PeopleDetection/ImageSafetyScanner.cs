using PrivacyAudit.Core;

namespace PrivacyAudit.PeopleDetection;

public sealed class ImageSafetyScanner(ModelManager modelManager, ImageSafetyRepository repository)
{
    public async Task<IReadOnlyList<ImageSafetyScanResult>> ScanAsync(IEnumerable<Finding> findings, bool forceRescan = false, IProgress<ImageSafetyScanProgress>? progress = null, CancellationToken token = default, Action<ImageSafetyScanResult>? onResult = null)
    {
        var files = findings.Where(x => x.Category is "Images" or "Video").ToArray();
        // GetVerifiedModelPath() checks SHA-256 synchronously; throws if not InstalledVerified.
        // Scanners must not contain any network logic — they only consume local verified paths.
        var verifiedPath = modelManager.GetVerifiedModelPath();
        var results = new List<ImageSafetyScanResult>(files.Length); var nsfw = 0; var nsfl = 0; var errors = 0;
        using var classifier = new ImageSafetyClassifier(verifiedPath);
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
                    var score = finding.Category == "Video"
                        ? await ClassifyVideoAsync(classifier, modelManager, finding.Path, token)
                        : await Task.Run(() => classifier.Classify(finding.Path, token), token);
                    result = new(finding.Path, ImageSafetyScanStatus.Completed, score.PrimaryClass, score.Nsfl, score.Nsfw, score.Sfw, modelManager.Manifest.ModelVersion, DateTime.UtcNow, file.Length, file.LastWriteTime);
                }
                catch (OperationCanceledException) { throw; }
                catch (VideoDecodeException ex) { modelManager.LogVideoDecodeError(finding.Path, ex); result = Error(finding.Path, file.Length, file.LastWriteTime, modelManager.Manifest.ModelVersion, ex.Code switch
                    {
                        VideoDecodeCode.VideoNoDecoder => "VIDEO_NO_DECODER",
                        VideoDecodeCode.VideoUnsupported => "VIDEO_UNSUPPORTED",
                        VideoDecodeCode.VideoNoVideoStream => "VIDEO_NO_VIDEO_STREAM",
                        VideoDecodeCode.VideoDecodeTimeout => "VIDEO_DECODE_TIMEOUT",
                        _ => "VIDEO_DECODE_FAILED"
                    }); }
                catch (Exception ex) { modelManager.LogScanError("Image Safety", finding.Path, ex); result = Error(finding.Path, file.Length, file.LastWriteTime, modelManager.Manifest.ModelVersion, ex.Message); }
            }
            await Task.Run(() => repository.Upsert(result), token); results.Add(result); onResult?.Invoke(result);
            if (result.PrimaryClass == ImageSafetyClass.NSFW && result.Status == ImageSafetyScanStatus.Completed) nsfw++;
            if (result.PrimaryClass == ImageSafetyClass.NSFL && result.Status == ImageSafetyScanStatus.Completed) nsfl++;
            if (result.Status == ImageSafetyScanStatus.Error) errors++;
            progress?.Report(new(finding.Path, results.Count, files.Length, nsfw, nsfl, errors, result.Error));
        }
        return results;
    }

    static async Task<ImageSafetyScores> ClassifyVideoAsync(ImageSafetyClassifier classifier, ModelManager manager, string path, CancellationToken token)
    {
        using var samples = await VideoFrameSampler.SampleForClassificationAsync(path, token);
        foreach (var diagnostic in samples.Diagnostics) manager.LogVideoDecode(path, diagnostic);
        ImageSafetyScores? selected = null;
        foreach (var frame in samples.Frames)
        {
            token.ThrowIfCancellationRequested();
            var score = classifier.Classify(frame, token);
            if (selected is null || score.Nsfw > selected.Value.Nsfw) selected = score;
            if (score.Nsfw > ImageSafetyMetadata.NsfwFilterThreshold) break;
        }
        return selected ?? throw new InvalidDataException("No video frame could be decoded.");
    }
    static ImageSafetyScanResult Error(string path, long size, DateTime modified, string version, string error) => new(path, ImageSafetyScanStatus.Error, ImageSafetyClass.SFW, 0, 0, 0, version, DateTime.UtcNow, size, modified, error);
}
