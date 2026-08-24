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
        Assert.StartsWith("https://github.com/opencv/opencv_zoo/", manifest.Url, StringComparison.Ordinal);
        Assert.Equal("yunet-2026may", manifest.ModelVersion);
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
            var exception = await Assert.ThrowsAsync<ModelDownloadException>(() => manager.EnsureInstalledDetailedAsync(new Progress<ModelDownloadProgress>(p => progress.Add(p.Stage))));
            Assert.Equal("timeout", exception.Code);
            Assert.Contains(ModelDownloadStage.Connecting, progress);
            Assert.True(File.Exists(manager.LogPath));
            Assert.Contains("timed out", File.ReadAllText(manager.LogPath), StringComparison.OrdinalIgnoreCase);
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

    sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }
}
