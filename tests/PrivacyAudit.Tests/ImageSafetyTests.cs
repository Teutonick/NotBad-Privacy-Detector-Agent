using PrivacyAudit.Core;
using PrivacyAudit.PeopleDetection;

namespace PrivacyAudit.Tests;

public sealed class ImageSafetyTests
{
    [Theory]
    [InlineData("Images", true, false, true)]
    [InlineData("Video", true, false, false)]
    [InlineData("Images", false, true, false)]
    [InlineData("Video", false, true, true)]
    [InlineData("Video", true, true, true)]
    [InlineData("Images", false, false, false)]
    public void NsfwScopeSelectsOnlyRequestedMedia(string category, bool images, bool videos, bool expected) =>
        Assert.Equal(expected, NsfwMediaScope.Matches(category, images, videos));

    [Theory]
    [InlineData("AllMedia", true)]
    [InlineData("People", true)]
    [InlineData("NSFW", true)]
    [InlineData("NoPeople", true)]
    [InlineData("Errors", true)]
    [InlineData("Unscanned", true)]
    [InlineData("GeoExif", false)]
    [InlineData("GpsOnly", false)]
    [InlineData("Documents", false)]
    public void MediaCategoryDeclaresWhetherImageVideoScopeApplies(string category, bool expected) =>
        Assert.Equal(expected, MediaCategoryFilter.UsesTypeScope(category));

    [Fact]
    public void ManifestPinsOptionalMitOnnxPackage()
    {
        var manifest = ModelManifest.ImageSafetyXs;
        Assert.Equal("ImageSafety", manifest.PackageDirectory);
        Assert.Equal("image-safety-classifier-xs.onnx", manifest.File);
        Assert.Equal("MIT", manifest.License);
        Assert.Equal("8C28C49D9075F3AD15EBDC2961F02D5B3F99BE944815B848B49C9F0E6F3FB689", manifest.Sha256);
        // Both models must serve from immutable pinned-commit URLs in the project repository.
        Assert.StartsWith("https://raw.githubusercontent.com/Teutonick/InfoSec-AUDIT-LOCAL/", manifest.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("/main/", manifest.Url, StringComparison.Ordinal);
        Assert.True(manifest.ExpectedSize > 0);
    }

    [Theory]
    [InlineData(.8, .1, .1, ImageSafetyClass.NSFL)]
    [InlineData(.1, .8, .1, ImageSafetyClass.NSFW)]
    [InlineData(.1, .2, .7, ImageSafetyClass.SFW)]
    public void PrimaryClassUsesThreeWayArgmax(double nsfl, double nsfw, double sfw, ImageSafetyClass expected) =>
        Assert.Equal(expected, new ImageSafetyScores(nsfl, nsfw, sfw).PrimaryClass);

    [Fact]
    public void MetadataPreservesAllScoresAlongsideExistingMetadata()
    {
        var result = new ImageSafetyScanResult("photo.jpg", ImageSafetyScanStatus.Completed, ImageSafetyClass.NSFW,
            .02, .93, .05, "image-safety-classifier-xs-606ad3d", DateTime.UtcNow, 123, DateTime.Now);
        var json = ImageSafetyMetadata.InjectIntoMetadata("{\"existing\":true}", result);
        Assert.Contains("\"existing\":true", json);
        Assert.True(ImageSafetyMetadata.TryParse(json, out var parsed));
        Assert.Equal(.93, parsed!.NsfwScore, 5);
        Assert.Equal(.02, parsed.NsflScore, 5);
        Assert.Equal(.05, parsed.SfwScore, 5);
        Assert.True(ImageSafetyMetadata.IsHighConfidenceNsfw(json));
    }

    [Theory]
    [InlineData(.85, false)]
    [InlineData(.85001, true)]
    [InlineData(.99, true)]
    public void NsfwFilterUsesStrictEightyFivePercentThreshold(double score, bool expected)
    {
        var result = new ImageSafetyScanResult("photo.jpg", ImageSafetyScanStatus.Completed, ImageSafetyClass.NSFW,
            0, score, 1 - score, "xs", DateTime.UtcNow, 1, DateTime.Now);
        Assert.Equal(expected, ImageSafetyMetadata.IsHighConfidenceNsfw(ImageSafetyMetadata.InjectIntoMetadata(null, result)));
    }

    [Fact]
    public void VideoSamplingUsesAtMostTwoBoundedFrames()
    {
        Assert.Equal([TimeSpan.FromMilliseconds(750)], VideoFrameSampler.SelectSamplePositions(TimeSpan.FromSeconds(1.5)));
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)], VideoFrameSampler.SelectSamplePositions(TimeSpan.FromSeconds(4)));
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(6)], VideoFrameSampler.SelectSamplePositions(TimeSpan.FromSeconds(12)));
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)], VideoFrameSampler.UnknownDurationPositions());
        Assert.Equal(VideoFrameSampler.UnknownDurationPositions(), VideoFrameSampler.SelectSamplePositions(TimeSpan.Zero));
    }

    [Fact]
    public void VideoPreviewUsesExactlyOneBoundedFrame()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), VideoFrameSampler.SelectPreviewPosition(TimeSpan.FromSeconds(1.2)));
        Assert.Equal(TimeSpan.FromSeconds(1), VideoFrameSampler.SelectPreviewPosition(TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(1), VideoFrameSampler.SelectPreviewPosition(null));
    }

    [Fact]
    public void RepositoryReusesOnlyUnchangedImageAndMatchingModel()
    {
        var root = Path.Combine(Path.GetTempPath(), $"privacy-audit-safety-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        try
        {
            var repository = new ImageSafetyRepository(Path.Combine(root, "audit.db")); var modified = DateTime.Now;
            var result = new ImageSafetyScanResult("photo.jpg", ImageSafetyScanStatus.Completed, ImageSafetyClass.SFW, .01, .02, .97, "v1", DateTime.UtcNow, 100, modified);
            repository.Upsert(result);
            Assert.NotNull(repository.FindReusable("photo.jpg", 100, modified, "v1"));
            Assert.Null(repository.FindReusable("photo.jpg", 101, modified, "v1"));
            Assert.Null(repository.FindReusable("photo.jpg", 100, modified, "v2"));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InstallingXsRemovesObsoleteModelFileOnlyAfterVerification()
    {
        var root = Path.Combine(Path.GetTempPath(), $"privacy-audit-safety-package-{Guid.NewGuid():N}");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        // New manifest signature: no LicenseUrl / MirrorUrl; ExpectedSize added.
        var manifest = new ModelManifest("xs", "test", "MIT", "xs.onnx", "https://example.invalid/xs.onnx", digest, bytes.LongLength, "ImageSafety", "XS", "test");
        try
        {
            using var client = new HttpClient(new BytesHandler(bytes));
            var manager = new ModelManager(root, manifest, client);
            Directory.CreateDirectory(manager.DirectoryPath);
            var obsolete = Path.Combine(manager.DirectoryPath, "image-safety-classifier-m.onnx");
            await File.WriteAllTextAsync(obsolete, "old");
            await manager.InstallAsync();
            Assert.True(File.Exists(manager.ModelPath));
            Assert.False(File.Exists(obsolete));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}
