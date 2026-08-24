using System.Net.Http;
using System.Text.Json;
using PrivacyAudit.Core;

namespace PrivacyAudit.PeopleDetection;

public sealed class ModelManager
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    readonly HttpClient _httpClient;
    readonly ModelIntegrityVerifier _integrityVerifier;
    readonly object _logGate = new();

    public ModelManager(string applicationDataDirectory, ModelManifest? manifest = null, HttpClient? httpClient = null, ModelIntegrityVerifier? integrityVerifier = null, TimeSpan? downloadTimeout = null)
    {
        Manifest = manifest ?? ModelManifest.YuNet2026May;
        DirectoryPath = Path.Combine(applicationDataDirectory, "Models", "YuNet");
        LogPath = Path.Combine(applicationDataDirectory, "people-model.log");
        DownloadTimeout = downloadTimeout ?? TimeSpan.FromSeconds(45);
        _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _integrityVerifier = integrityVerifier ?? new ModelIntegrityVerifier();
    }

    public ModelManifest Manifest { get; }
    public string DirectoryPath { get; }
    public string ModelPath => Path.Combine(DirectoryPath, Manifest.File);
    public string LicensePath => Path.Combine(DirectoryPath, "LICENSE.txt");
    public string MetadataPath => Path.Combine(DirectoryPath, "model.json");
    public string LogPath { get; }
    public TimeSpan DownloadTimeout { get; }
    public bool HasModelFiles => Directory.Exists(DirectoryPath);

    public bool IsInstalled => _integrityVerifier.Verify(ModelPath, Manifest);

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) => await _integrityVerifier.VerifyAsync(ModelPath, Manifest, cancellationToken);

    public async Task<string> EnsureInstalledAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        => await EnsureInstalledDetailedAsync(new Progress<ModelDownloadProgress>(x => { if (x.Fraction is double fraction) progress?.Report(fraction); }), cancellationToken);

    public async Task<string> EnsureInstalledDetailedAsync(IProgress<ModelDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(new(ModelDownloadStage.Checking));
        Log("Checking the installed model.");
        if (await IsInstalledAsync(cancellationToken))
        {
            progress?.Report(new(ModelDownloadStage.Completed, 1, 1));
            Log("The installed model passed SHA-256 verification.");
            return ModelPath;
        }

        Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = Path.Combine(DirectoryPath, $"{Manifest.File}.{Guid.NewGuid():N}.download");
        try
        {
            progress?.Report(new(ModelDownloadStage.Connecting));
            Log($"Connecting to the official model URL. Timeout: {DownloadTimeout.TotalSeconds:0} seconds. URL: {Manifest.Url}");
            using var timeout = new CancellationTokenSource(DownloadTimeout);
            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var response = await _httpClient.GetAsync(Manifest.Url, HttpCompletionOption.ResponseHeadersRead, linkedToken.Token);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            progress?.Report(new(ModelDownloadStage.Downloading, 0, total));
            await using (var source = await response.Content.ReadAsStreamAsync(linkedToken.Token))
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                long copied = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, linkedToken.Token)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), linkedToken.Token);
                    copied += read;
                    progress?.Report(new(ModelDownloadStage.Downloading, copied, total));
                }
            }

            progress?.Report(new(ModelDownloadStage.Verifying, total ?? 0, total));
            Log("Download finished. Verifying SHA-256.");
            if (!await _integrityVerifier.VerifyAsync(temporaryPath, Manifest, linkedToken.Token))
                throw new ModelDownloadException("The downloaded YuNet model failed SHA-256 verification.", "hash_mismatch");

            progress?.Report(new(ModelDownloadStage.DownloadingLicense, total ?? 0, total));
            Log("Downloading the official MIT license copy.");
            var license = await _httpClient.GetStringAsync(Manifest.LicenseUrl, linkedToken.Token);
            if (!license.Contains("MIT License", StringComparison.OrdinalIgnoreCase))
                throw new ModelDownloadException("The downloaded model license is not the expected MIT license.", "license_mismatch");

            progress?.Report(new(ModelDownloadStage.Installing, total ?? 0, total));
            File.Move(temporaryPath, ModelPath, true);
            await File.WriteAllTextAsync(LicensePath, license, linkedToken.Token);
            var metadata = new ModelMetadata("YuNet", Manifest.Version, Manifest.License, Manifest.Sha256, "opencv/opencv_zoo");
            await File.WriteAllTextAsync(MetadataPath, JsonSerializer.Serialize(metadata, JsonOptions), linkedToken.Token);
            progress?.Report(new(ModelDownloadStage.Completed, total ?? 1, total ?? 1));
            Log("Model installation completed successfully.");
            return ModelPath;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var error = new ModelDownloadException($"The model download timed out after {DownloadTimeout.TotalSeconds:0} seconds. Check network access and try again.", "timeout");
            Log(error.Message, error);
            throw error;
        }
        catch (Exception ex)
        {
            Log("Model installation failed.", ex);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public void RemoveInstalledModel()
    {
        if (!Directory.Exists(DirectoryPath)) return;
        try
        {
            Directory.Delete(DirectoryPath, true);
            Log("The local model was removed by the user.");
        }
        catch (IOException ex)
        {
            Log("The local model could not be removed because it is in use.", ex);
            throw new ModelDownloadException("The local model is currently in use by PrivacyAudit or another process.", "model_in_use", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log("The local model could not be removed because access was denied.", ex);
            throw new ModelDownloadException("Access to the local model folder was denied.", "model_access_denied", ex);
        }
    }

    void Log(string message, Exception? exception = null)
    {
        try
        {
            lock (_logGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {message}{(exception is null ? "" : $" {exception.GetType().Name}: {exception.Message}")}\r\n");
                StorageLimits.TrimTextLog(LogPath, StorageLimits.MaxDiagnosticLogBytes);
            }
        }
        catch { /* Diagnostics must never stop the audit. */ }
    }

    public void LogPeopleScanError(string path, Exception exception) => Log($"People scan failed for '{path}'.", exception);

    sealed record ModelMetadata(string Model, string Version, string License, string Sha256, string Source);
}
