using System.IO;
using PrivacyAudit.Core;
using PrivacyAudit.PeopleDetection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PrivacyAudit.Tests;

public sealed class SimilarityTests
{
    [Fact]
    public void DocumentSimilarity_MatchesSimilarTextFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sim_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var file1 = Path.Combine(tempDir, "passport_backup.txt");
            var file2 = Path.Combine(tempDir, "person_archive.txt");
            var file3 = Path.Combine(tempDir, "recipe.txt");

            File.WriteAllText(file1, "Серия и номер паспорта РФ: 4510 123456, выдан ОУФМС города Москвы. Владелец: Иванов Иван Иванович, СНИЛС 112-233-445 95.");
            File.WriteAllText(file2, "Паспортные данные гражданина: Иванов Иван Иванович, паспорт серия 4510 номер 123456, СНИЛС: 112-233-445 95, адрес: Москва.");
            File.WriteAllText(file3, "Ингредиенты для пирога: мука 500 грамм, сахар 200 грамм, три яйца, сливочное масло и яблоки.");

            var finding1 = new Finding
            {
                ScannerId = "test",
                Path = file1,
                DisplayName = Path.GetFileName(file1),
                Category = "Documents",
                ExposureReasons = []
            };

            var finding2 = new Finding
            {
                ScannerId = "test",
                Path = file2,
                DisplayName = Path.GetFileName(file2),
                Category = "Documents",
                ExposureReasons = []
            };

            var finding3 = new Finding
            {
                ScannerId = "test",
                Path = file3,
                DisplayName = Path.GetFileName(file3),
                Category = "Documents",
                ExposureReasons = []
            };

            var matches = DocumentSimilarity.FindSimilar(finding1, [finding1, finding2, finding3]);

            Assert.NotEmpty(matches);
            var topMatch = matches[0];
            Assert.Equal(file2, topMatch.Finding.Path);
            Assert.True(topMatch.Score >= 0.50);

            // Recipe should either not match or have a much lower score
            var recipeMatch = matches.FirstOrDefault(m => m.Finding.Path == file3);
            if (recipeMatch is not null)
            {
                Assert.True(topMatch.Score > recipeMatch.Score * 2);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ImageSimilarity_MatchesOriginalAndResizedCopy()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"img_sim_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var origPath = Path.Combine(tempDir, "photo_orig.png");
            var copyPath = Path.Combine(tempDir, "photo_copy.png");
            var otherPath = Path.Combine(tempDir, "photo_diff.png");

            // 1. Create original gradient image (300x200)
            using (var img = new Image<Rgb24>(300, 200))
            {
                for (int y = 0; y < 200; y++)
                {
                    for (int x = 0; x < 300; x++)
                    {
                        img[x, y] = new Rgb24((byte)(x % 256), (byte)(y % 256), (byte)((x + y) % 256));
                    }
                }
                img.SaveAsPng(origPath);
            }

            // 2. Create scaled-down copy (150x100)
            using (var img = new Image<Rgb24>(150, 100))
            {
                for (int y = 0; y < 100; y++)
                {
                    for (int x = 0; x < 150; x++)
                    {
                        img[x, y] = new Rgb24((byte)((x * 2) % 256), (byte)((y * 2) % 256), (byte)(((x + y) * 2) % 256));
                    }
                }
                img.SaveAsPng(copyPath);
            }

            // 3. Create completely different solid image
            using (var img = new Image<Rgb24>(300, 200))
            {
                for (int y = 0; y < 200; y++)
                {
                    for (int x = 0; x < 300; x++)
                    {
                        img[x, y] = new Rgb24(200, 50, 50);
                    }
                }
                img.SaveAsPng(otherPath);
            }

            var f1 = new Finding { ScannerId = "t", Path = origPath, DisplayName = "orig", Category = "Images", ExposureReasons = [] };
            var f2 = new Finding { ScannerId = "t", Path = copyPath, DisplayName = "copy", Category = "Images", ExposureReasons = [] };
            var f3 = new Finding { ScannerId = "t", Path = otherPath, DisplayName = "diff", Category = "Images", ExposureReasons = [] };

            var matches = ImageSimilarity.FindSimilar(f1, [f1, f2, f3]);

            Assert.NotEmpty(matches);
            Assert.Equal(copyPath, matches[0].Finding.Path);
            Assert.True(matches[0].Score >= 0.85);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ExifMetadataExtractor_ExtractsMetadataGracefully()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"meta_test_{Guid.NewGuid():N}.png");
        try
        {
            using (var img = new Image<Rgb24>(100, 100))
            {
                img.SaveAsPng(tempFile);
            }

            var result = ExifMetadataExtractor.Extract(tempFile);
            Assert.NotNull(result);
            Assert.False(result.HasGeolocation);
            Assert.Equal("Low", result.ExposureLevel);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SimilarityAnalysisResult_RoundTripsThroughFindingMetadata()
    {
        var completedAt = DateTime.UtcNow;
        var metadata = SimilarityAnalysisResult.InjectIntoMetadata("{\"keep\":true}", new SimilarityAnalysisResult
        {
            Kind = "Image",
            CompletedAtUtc = completedAt,
            Matches = [new SavedSimilarityMatch("C:\\copy.png", 0.91, "perceptual match")]
        });

        Assert.True(SimilarityAnalysisResult.TryParse(metadata, out var restored));
        Assert.Equal("Image", restored!.Kind);
        Assert.Single(restored.Matches);
        Assert.Equal("C:\\copy.png", restored.Matches[0].Path);
        Assert.Contains("\"keep\":true", metadata);
    }
}
