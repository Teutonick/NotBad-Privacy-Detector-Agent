using System.Net.Http;
using System.Text.Json;
using PrivacyAudit.Core;

namespace PrivacyAudit.PeopleDetection;

/// <summary>
/// Manages the lifecycle of a single optional ONNX model: checking its status,
/// downloading it on explicit user request, verifying its SHA-256 integrity, and
/// removing it on explicit user request.
///
/// Design constraints (see docs/ARCHITECTURE.md § Model Download Safety):
/// • Downloads only on an explicit call to <see cref="InstallAsync"/> — never
///   automatically at startup, in the background, or on a timer.
/// • Uses a single standard <see cref="HttpClient"/> with system TLS; no custom
///   certificate validation, no HTTP fallback, no shell processes.
/// • The temporary file has a fixed, predictable name (<c>.download</c> suffix)
///   and is stored in the model directory — never in %TEMP%.
/// • Size is bounded by <see cref="ModelManifest.MaximumAllowedSize"/> before and
///   during the download; the stream is aborted if the limit is exceeded.
/// • SHA-256 is verified against the manifest before atomic rename; the temporary
///   file is deleted on any failure or cancellation.
/// • The ONNX file is never modified after installation.
/// • Removal validates the target path is strictly inside the Models root before
///   deleting.
/// • No AV exclusions, Defender preferences, or Zone.Identifier manipulation.
/// </summary>
public sealed class ModelManager
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    readonly HttpClient _httpClient;
    readonly ModelIntegrityVerifier _integrityVerifier;
    readonly string _applicationDataDirectory;
    readonly string _modelsRoot;
    readonly object _logGate = new();

    public ModelManager(string applicationDataDirectory, ModelManifest? manifest = null, HttpClient? httpClient = null, ModelIntegrityVerifier? integrityVerifier = null, TimeSpan? downloadTimeout = null)
    {
        _applicationDataDirectory = applicationDataDirectory;
        _modelsRoot = Path.GetFullPath(Path.Combine(applicationDataDirectory, "Models"));
        Manifest = manifest ?? ModelManifest.YuNet2026May;
        DirectoryPath = Path.Combine(_modelsRoot, Manifest.PackageDirectory);
        LogPath = Path.Combine(applicationDataDirectory, Manifest.PackageDirectory == "YuNet" ? "people-model.log" : $"{Manifest.Id}-model.log");
        DownloadTimeout = downloadTimeout ?? TimeSpan.FromSeconds(120);
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

    // ------------------------------------------------------------------
    // Status API
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the current model status by checking file existence and, if the
    /// file is present, verifying its SHA-256 synchronously.
    /// Scanners call this to confirm the model is safe to use for inference.
    /// </summary>
    public ModelStatus GetStatus()
    {
        if (!File.Exists(ModelPath)) return ModelStatus.NotInstalled;
        return _integrityVerifier.Verify(ModelPath, Manifest)
            ? ModelStatus.InstalledVerified
            : ModelStatus.Corrupted;
    }

    /// <summary>Async variant used by the UI to avoid blocking the dispatcher.</summary>
    public async Task<ModelStatus> CheckStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ModelPath)) return ModelStatus.NotInstalled;
        return await _integrityVerifier.VerifyAsync(ModelPath, Manifest, cancellationToken)
            ? ModelStatus.InstalledVerified
            : ModelStatus.Corrupted;
    }

    /// <summary>
    /// Returns the path to the verified ONNX file.
    /// Throws <see cref="ModelDownloadException"/> if the model is not in
    /// <see cref="ModelStatus.InstalledVerified"/> state.
    /// Scanners must call this instead of accessing <see cref="ModelPath"/> directly.
    /// </summary>
    public string GetVerifiedModelPath()
    {
        var status = GetStatus();
        if (status == ModelStatus.InstalledVerified) return ModelPath;
        throw new ModelDownloadException(
            status == ModelStatus.Corrupted
                ? $"The {Manifest.DisplayName} model is corrupted or has been modified. Download it again."
                : $"The {Manifest.DisplayName} model is not installed.",
            status == ModelStatus.Corrupted ? "model_corrupted" : "model_missing");
    }

    // Keep a lightweight bool-returning async helper for UI refresh paths that
    // only need a true/false answer without handling Corrupted specially.
    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
        => await CheckStatusAsync(cancellationToken) == ModelStatus.InstalledVerified;

    // ------------------------------------------------------------------
    // Install
    // ------------------------------------------------------------------

    /// <summary>
    /// Downloads, verifies, and installs the model.
    /// Must be called only in response to an explicit user action — never
    /// automatically at startup, on a timer, or in the background.
    /// </summary>
    public async Task InstallAsync(IProgress<ModelDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // If already verified, nothing to do.
        progress?.Report(new(ModelDownloadStage.Checking));
        LogEvent("MODEL_CHECK_STARTED", $"model={Manifest.Id}");

        if (await CheckStatusAsync(cancellationToken) == ModelStatus.InstalledVerified)
        {
            progress?.Report(new(ModelDownloadStage.Completed, 1, 1));
            LogEvent("MODEL_ALREADY_INSTALLED", $"model={Manifest.Id}");
            return;
        }

        // Enforce HTTPS before opening any connection.
        if (!Manifest.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new ModelDownloadException("The model URL must use HTTPS.", "invalid_url");

        Directory.CreateDirectory(DirectoryPath);

        // Predictable, fixed temporary filename — no random GUIDs.
        // Stored in the same directory as the final file so rename is atomic.
        var temporaryPath = Path.Combine(DirectoryPath, $"{Manifest.File}.download");

        // Remove any leftover from a previously cancelled download.
        if (File.Exists(temporaryPath)) File.Delete(temporaryPath);

        LogEvent("MODEL_DOWNLOAD_STARTED", $"model={Manifest.Id} url={Manifest.Url} expected_bytes={Manifest.ExpectedSize} maximum_bytes={Manifest.MaximumAllowedSize}");

        try
        {
            progress?.Report(new(ModelDownloadStage.Connecting));

            using var timeout = new CancellationTokenSource(DownloadTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            using var response = await _httpClient.GetAsync(
                Manifest.Url,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token);
            response.EnsureSuccessStatusCode();

            if (response.RequestMessage?.RequestUri is Uri finalUri
                && !finalUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new ModelDownloadException("The model source redirected to a non-HTTPS address. The download was aborted.", "invalid_redirect");

            // --- Size guard (Content-Length) ---
            var contentLength = response.Content.Headers.ContentLength;
            progress?.Report(new(ModelDownloadStage.SizeCheck, 0, contentLength));

            if (contentLength.HasValue && contentLength.Value > Manifest.MaximumAllowedSize)
            {
                LogEvent("MODEL_SIZE_EXCEEDED", $"model={Manifest.Id} reported={contentLength.Value} maximum={Manifest.MaximumAllowedSize}");
                throw new ModelDownloadException(
                    $"The server reported a file size of {contentLength.Value:N0} bytes, which exceeds the allowed maximum of {Manifest.MaximumAllowedSize:N0} bytes. The download was aborted.",
                    "size_exceeded");
            }

            progress?.Report(new(ModelDownloadStage.Downloading, 0, contentLength));

            // --- Streaming download with runtime size guard ---
            await using (var source = await response.Content.ReadAsStreamAsync(linked.Token))
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                long copied = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, linked.Token)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), linked.Token);
                    copied += read;

                    // Runtime guard: abort if server sends more than allowed.
                    if (copied > Manifest.MaximumAllowedSize)
                    {
                        LogEvent("MODEL_SIZE_EXCEEDED", $"model={Manifest.Id} received={copied} maximum={Manifest.MaximumAllowedSize}");
                        throw new ModelDownloadException(
                            $"The download exceeded the allowed maximum size of {Manifest.MaximumAllowedSize:N0} bytes and was aborted.",
                            "size_exceeded");
                    }

                    progress?.Report(new(ModelDownloadStage.Downloading, copied, contentLength));
                }
            }

            var downloadedBytes = new FileInfo(temporaryPath).Length;
            if (downloadedBytes != Manifest.ExpectedSize)
            {
                LogEvent("MODEL_SIZE_MISMATCH", $"model={Manifest.Id} received={downloadedBytes} expected={Manifest.ExpectedSize}");
                throw new ModelDownloadException(
                    $"The downloaded file size ({downloadedBytes:N0} bytes) does not match the pinned model size ({Manifest.ExpectedSize:N0} bytes). The file was deleted.",
                    "size_mismatch");
            }
            LogEvent("MODEL_DOWNLOAD_COMPLETE", $"model={Manifest.Id} bytes={downloadedBytes}");

            // --- SHA-256 verification ---
            progress?.Report(new(ModelDownloadStage.Verifying, downloadedBytes, downloadedBytes));
            if (!await _integrityVerifier.VerifyAsync(temporaryPath, Manifest, linked.Token))
            {
                LogEvent("MODEL_HASH_MISMATCH", $"model={Manifest.Id} expected_sha256={Manifest.Sha256}");
                throw new ModelDownloadException(
                    $"The downloaded {Manifest.DisplayName} model failed SHA-256 verification. The file was deleted.",
                    "hash_mismatch");
            }
            LogEvent("MODEL_HASH_VERIFIED", $"model={Manifest.Id} sha256={Manifest.Sha256}");

            // --- Atomic installation ---
            progress?.Report(new(ModelDownloadStage.Installing, downloadedBytes, downloadedBytes));
            RemoveObsoletePackageModels();

            // Rename is atomic within the same volume; temporary and final paths
            // share the same directory, so this is always an intra-volume rename.
            File.Move(temporaryPath, ModelPath, overwrite: true);

            // Write sidecar files AFTER the ONNX is in place.
            // The ONNX itself is never modified — byte-for-byte identical to what
            // was verified.
            var metadata = new ModelMetadata(Manifest.DisplayName, Manifest.Version, Manifest.License, Manifest.Sha256, Manifest.Source);
            await File.WriteAllTextAsync(MetadataPath, JsonSerializer.Serialize(metadata, JsonOptions), linked.Token);
            await File.WriteAllTextAsync(LicensePath, EmbeddedMitLicense(), linked.Token);

            progress?.Report(new(ModelDownloadStage.Completed, downloadedBytes, downloadedBytes));
            LogEvent("MODEL_INSTALLED", $"model={Manifest.Id} path={ModelPath}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout branch (user did not cancel explicitly).
            LogEvent("MODEL_DOWNLOAD_TIMEOUT", $"model={Manifest.Id} timeout_seconds={DownloadTimeout.TotalSeconds:0}");
            throw new ModelDownloadException(
                $"The model download timed out after {DownloadTimeout.TotalSeconds:0} seconds. Check your network connection and try again.",
                "timeout");
        }
        catch (HttpRequestException ex) when (IsTlsError(ex))
        {
            LogEvent("MODEL_TLS_ERROR", $"model={Manifest.Id} error={ex.Message}");
            throw new ModelDownloadException(
                "The model download was aborted because the TLS/SSL connection could not be established. The server certificate may be invalid.",
                "tls_error", ex);
        }
        catch (OperationCanceledException)
        {
            LogEvent("MODEL_DOWNLOAD_CANCELLED", $"model={Manifest.Id}");
            throw;
        }
        catch (Exception ex) when (ex is not ModelDownloadException)
        {
            LogEvent("MODEL_DOWNLOAD_FAILED", $"model={Manifest.Id} error={ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            // Always clean up the temporary file — on success it has been renamed
            // away; on failure/cancel it must not remain on disk.
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch { /* Best-effort cleanup. */ }
            }
        }
    }

    // ------------------------------------------------------------------
    // Remove
    // ------------------------------------------------------------------

    /// <summary>
    /// Removes the installed model directory.
    /// The target path is normalised and validated to be strictly inside the
    /// Models root before any deletion occurs.
    /// </summary>
    public void RemoveInstalledModel()
    {
        if (!Directory.Exists(DirectoryPath)) return;

        // Path-traversal guard: the directory being deleted must be a direct
        // child of the application Models root.
        var normalised = Path.GetFullPath(DirectoryPath);
        var normalRoot = Path.GetFullPath(_modelsRoot);
        if (!normalised.StartsWith(normalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !normalised.Equals(normalRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ModelDownloadException(
                "The model directory path is outside the expected Models folder. Removal was aborted for safety.",
                "path_traversal");
        }

        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
            LogEvent("MODEL_REMOVED", $"model={Manifest.Id}");
        }
        catch (IOException ex)
        {
            Log($"The local model could not be removed because it is in use.", ex);
            throw new ModelDownloadException("The local model is currently in use by PrivacyAudit or another process.", "model_in_use", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log($"The local model could not be removed because access was denied.", ex);
            throw new ModelDownloadException("Access to the local model folder was denied.", "model_access_denied", ex);
        }
    }

    // ------------------------------------------------------------------
    // Diagnostic logging helpers
    // ------------------------------------------------------------------

    void RemoveObsoletePackageModels()
    {
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.onnx", SearchOption.TopDirectoryOnly))
        {
            if (!string.Equals(path, ModelPath, StringComparison.OrdinalIgnoreCase))
                try { File.Delete(path); } catch { /* Best-effort. */ }
        }
    }

    /// <summary>Writes a structured event line to the model log.</summary>
    void LogEvent(string eventCode, string? detail = null)
    {
        var line = detail is null
            ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {eventCode}"
            : $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {eventCode} {detail}";
        Log(line);
    }

    void Log(string message, Exception? exception = null)
    {
        try
        {
            lock (_logGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{message}{(exception is null ? "" : $" {exception.GetType().Name}: {exception.Message}")}\r\n");
                StorageLimits.TrimTextLog(LogPath, StorageLimits.MaxDiagnosticLogBytes);
            }
        }
        catch { /* Diagnostics must never stop the audit. */ }
    }

    public void LogPeopleScanError(string path, Exception exception) => Log($"People scan failed for '{path}'.", exception);
    public void LogScanError(string scanName, string path, Exception exception) => Log($"{scanName} scan failed for '{path}'.", exception);

    public void LogVideoDecode(string path, VideoFrameDiagnostic diagnostic)
    {
        var duration = diagnostic.Duration is { } d ? $"{d.TotalSeconds:0.###} s" : "unknown (VIDEO_DURATION_UNKNOWN)";
        var actual = diagnostic.Actual is { } a ? $"{a.TotalSeconds:0.###} s" : "n/a";
        var resolution = diagnostic.Width > 0 ? $"{diagnostic.Width}x{diagnostic.Height}" : "n/a";
        var result = diagnostic.Code is { } code ? VideoDecodeCodeText(code) : diagnostic.Result;
        Log($"Video decode | File: '{path}' | Duration: {duration} | Requested: {diagnostic.Requested.TotalSeconds:0.###} s | Decoded: {actual} | Resolution: {resolution} | Decode: {diagnostic.DecodeTime.TotalMilliseconds:0} ms | Result: {result}");
    }

    public void LogVideoDecodeError(string path, VideoDecodeException exception)
    {
        foreach (var diagnostic in exception.Diagnostics) LogVideoDecode(path, diagnostic);
        Log($"Video decode skipped | File: '{path}' | Result: {VideoDecodeCodeText(exception.Code)} | {exception.Message}");
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    static string VideoDecodeCodeText(VideoDecodeCode code) => code switch
    {
        VideoDecodeCode.VideoNoDecoder => "VIDEO_NO_DECODER",
        VideoDecodeCode.VideoUnsupported => "VIDEO_UNSUPPORTED",
        VideoDecodeCode.VideoNoVideoStream => "VIDEO_NO_VIDEO_STREAM",
        VideoDecodeCode.VideoDecodeTimeout => "VIDEO_DECODE_TIMEOUT",
        _ => "VIDEO_DECODE_FAILED"
    };

    static bool IsTlsError(HttpRequestException ex)
    {
        var msg = ex.Message;
        return msg.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("TLS", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("certificate", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("trust", StringComparison.OrdinalIgnoreCase)
            || ex.InnerException?.Message.Contains("AuthenticationException", StringComparison.OrdinalIgnoreCase) == true;
    }

    string EmbeddedMitLicense() => MitLicenseTemplate.Replace(
        "{AUTHOR}",
        Manifest.Source.StartsWith("opencv/", StringComparison.OrdinalIgnoreCase) ? "OpenCV team" : "Owen Elliott",
        StringComparison.Ordinal);

    sealed record ModelMetadata(string Model, string Version, string License, string Sha256, string Source);

    const string MitLicenseTemplate = """
        MIT License

        Copyright (c) {AUTHOR}

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.
        """;
}
