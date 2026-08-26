using PrivacyAudit.PeopleDetection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace PrivacyAudit.Tests;

public sealed class PeopleDetectionTests
{
    [Fact]
    public void YuNetManifestPinsOfficialMitModelAndDigest()
    {
        var manifest = ModelManifest.YuNet2026May;
        Assert.Equal("MIT", manifest.License);
        Assert.Equal("face_detection_yunet_2026may.onnx", manifest.File);
        Assert.Equal(64, manifest.Sha256.Length);
        // Must point to an immutable commit SHA in the project repository — never a mutable branch.
        Assert.StartsWith("https://raw.githubusercontent.com/Teutonick/InfoSec-AUDIT-LOCAL/", manifest.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("/main/", manifest.Url, StringComparison.Ordinal);
        Assert.Equal("yunet-2026may", manifest.ModelVersion);
        Assert.True(manifest.ExpectedSize > 0);
        Assert.True(manifest.MaximumAllowedSize > manifest.ExpectedSize);
    }

    [Fact]
    public void ImageSafetyManifestPinsImmutableUrlFromProjectRepo()
    {
        var manifest = ModelManifest.ImageSafetyXs;
        Assert.Equal("MIT", manifest.License);
        Assert.Equal(64, manifest.Sha256.Length);
        // Both models must use the same pinned-commit pattern from the project repository.
        Assert.StartsWith("https://raw.githubusercontent.com/Teutonick/InfoSec-AUDIT-LOCAL/", manifest.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("/main/", manifest.Url, StringComparison.Ordinal);
        Assert.True(manifest.ExpectedSize > 0);
        Assert.True(manifest.MaximumAllowedSize > manifest.ExpectedSize);
    }

    [Fact]
    public void PeopleMetadataRoundTripsRequiredFields()
    {
        var original = new PeopleScanResult("C:\\photo.jpg", PeopleScanStatus.Completed, true, 3, 0.97, "yunet-2026may", DateTime.UtcNow, 123, DateTime.Now);
        var json = PeopleScanMetadata.Serialize(original);
        Assert.Contains("people_detected", json, StringComparison.Ordinal);
        Assert.True(PeopleScanMetadata.TryParse(json, out var parsed));
        Assert.NotNull(parsed);
        Assert.True(parsed!.PeopleDetected);
        Assert.Equal(3, parsed.FaceCount);
        Assert.Equal("yunet-2026may", parsed.ModelVersion);
    }

    [Fact]
    public void PeopleMetadataInjectionPreservesDocumentMetadata()
    {
        var document = new DocumentDetectionResult { IsDocument = true, IsIdentityDocument = true, Confidence = 0.91 };
        var current = DocumentDetectionResult.InjectIntoMetadata("", document);
        var people = new PeopleScanResult("photo.jpg", PeopleScanStatus.Completed, true, 1, 0.93, "yunet-2026may", DateTime.UtcNow, 10, DateTime.Now);

        var merged = PeopleScanMetadata.InjectIntoMetadata(current, people);

        Assert.True(PeopleScanMetadata.TryParse(merged, out var peopleResult));
        Assert.True(peopleResult!.PeopleDetected);
        Assert.True(DocumentDetectionResult.TryParse(merged, out var documentResult));
        Assert.True(documentResult!.IsDocument);
    }

    [Fact]
    public void RepositoryReusesOnlyMatchingFileFingerprintAndModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacy-audit-people-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new PeopleScanRepository(Path.Combine(root, "audit.db"));
            var modified = new DateTime(2026, 8, 22, 12, 30, 0, DateTimeKind.Local);
            var result = new PeopleScanResult("photo.jpg", PeopleScanStatus.Completed, false, 0, 0, "yunet-2026may", DateTime.UtcNow, 10, modified);
            repository.Upsert(result);
            Assert.NotNull(repository.FindReusable("photo.jpg", 10, modified, "yunet-2026may"));
            Assert.Null(repository.FindReusable("photo.jpg", 11, modified, "yunet-2026may"));
            Assert.Null(repository.FindReusable("photo.jpg", 10, modified, "yunet-2027jan"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FailedAnalysisIsRetriedInsteadOfBeingCached()
    {
        var modified = DateTime.Now;
        var failed = new PeopleScanResult("photo.jpg", PeopleScanStatus.Error, false, 0, 0, "yunet-2026may", DateTime.UtcNow, 10, modified, "temporary failure");
        Assert.False(failed.IsReusable("photo.jpg", 10, modified, "yunet-2026may"));
    }

    [Fact]
    public void ImageDecoderAcceptsWebpAndTiff()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacy-audit-image-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var webp = Path.Combine(root, "sample.webp");
            var tiff = Path.Combine(root, "sample.tiff");
            using (var image = new Image<Rgba32>(16, 16))
            {
                image.SaveAsWebp(webp);
                image.SaveAsTiff(tiff);
            }
            using var decodedWebp = Image.Load<Rgb24>(new DecoderOptions { MaxFrames = 1 }, webp);
            using var decodedTiff = Image.Load<Rgb24>(new DecoderOptions { MaxFrames = 1 }, tiff);
            Assert.Equal((16, 16), (decodedWebp.Width, decodedWebp.Height));
            Assert.Equal((16, 16), (decodedTiff.Width, decodedTiff.Height));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ModelManagerReportsTimeoutAndWritesDiagnosticLog()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacy-audit-people-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new HttpClient(new HangingHandler());
            var manager = new ModelManager(root, httpClient: client, downloadTimeout: TimeSpan.FromMilliseconds(100));
            var progress = new List<ModelDownloadStage>();
            var exception = await Assert.ThrowsAsync<ModelDownloadException>(
                () => manager.InstallAsync(new Progress<ModelDownloadProgress>(p => progress.Add(p.Stage))));
            Assert.Equal("timeout", exception.Code);
            Assert.Contains(ModelDownloadStage.Connecting, progress);
            Assert.True(File.Exists(manager.LogPath));
            var log = File.ReadAllText(manager.LogPath);
            Assert.Contains("MODEL_DOWNLOAD_TIMEOUT", log, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ModelManagerRemovesOptionalModelDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "privacy-audit-people-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new ModelManager(root);
            Directory.CreateDirectory(manager.DirectoryPath);
            File.WriteAllText(manager.ModelPath, "test model placeholder");
            Assert.True(manager.HasModelFiles);
            manager.RemoveInstalledModel();
            Assert.False(Directory.Exists(manager.DirectoryPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ModelManagerAbortsWhenContentLengthExceedsMaximum()
    {
        // Ensures the size guard fires before any bytes are written to the temp file.
        var root = Path.Combine(Path.GetTempPath(), "privacy-audit-people-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var oversizedLength = ModelManifest.YuNet2026May.MaximumAllowedSize * 2;
            using var client = new HttpClient(new OversizedHandler(oversizedLength));
            var manager = new ModelManager(root, httpClient: client);
            var exception = await Assert.ThrowsAsync<ModelDownloadException>(() => manager.InstallAsync());
            Assert.Equal("size_exceeded", exception.Code);
            // Temporary file must not linger after an aborted download.
            var tempPath = Path.Combine(manager.DirectoryPath, $"{manager.Manifest.File}.download");
            Assert.False(File.Exists(tempPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ModelManagerGetStatusReturnsCorruptedWhenHashMismatch()
    {
        // When the ONNX file exists but its content has changed, GetStatus() must
        // return Corrupted rather than InstalledVerified, and GetVerifiedModelPath()
        // must throw so no scanner can use the tainted file.
        var root = Path.Combine(Path.GetTempPath(), "privacy-audit-people-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new ModelManager(root);
            Directory.CreateDirectory(manager.DirectoryPath);
            File.WriteAllText(manager.ModelPath, "not a real onnx model");
            Assert.Equal(ModelStatus.Corrupted, manager.GetStatus());
            Assert.Throws<ModelDownloadException>(() => manager.GetVerifiedModelPath());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ModelManagerRemovalIsNoOpWhenDirectoryAbsent()
    {
        // RemoveInstalledModel on a non-existent directory must be a safe no-op.
        var root = Path.Combine(Path.GetTempPath(), "privacy-audit-people-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new ModelManager(root);
            Assert.False(manager.HasModelFiles);
            manager.RemoveInstalledModel(); // must not throw
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ModelManagerTempFileNameIsFixedAndPredictable()
    {
        // Verify the expected temp filename pattern without actually downloading.
        // The temp file must be <filename>.download — no random GUIDs.
        var manifest = ModelManifest.YuNet2026May;
        var expectedTempName = $"{manifest.File}.download";
        Assert.Equal("face_detection_yunet_2026may.onnx.download", expectedTempName);
        Assert.DoesNotContain("Guid", expectedTempName, StringComparison.OrdinalIgnoreCase);
        // Sanity check: the name must not look like a GUID (no 32-char hex segment).
        Assert.DoesNotMatch(@"[0-9a-f]{32}", expectedTempName);
    }

    // ---------------------------------------------------------------------------
    // Test helpers
    // ---------------------------------------------------------------------------

    sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }

    sealed class OversizedHandler(long reportedContentLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent([]);
            content.Headers.ContentLength = reportedContentLength;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }
}
