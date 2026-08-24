using System.IO;
using PrivacyAudit.PeopleDetection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PrivacyAudit.Tests;

public sealed class DocumentDetectorTests
{
    [Fact]
    public void DocumentDetector_NonExistentFile_ReturnsSafeResult()
    {
        var result = DocumentDetector.Analyze("non_existent_file.jpg");
        Assert.False(result.IsDocument);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public void DocumentDetector_ColorfulPhoto_IsNotClassifiedAsDocument()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"colorful_test_{Guid.NewGuid():N}.png");
        try
        {
            using (var img = new Image<Rgb24>(400, 300))
            {
                // Create vibrant colorful saturated image (e.g. blue sky and green grass)
                for (int y = 0; y < 300; y++)
                {
                    for (int x = 0; x < 400; x++)
                    {
                        img[x, y] = y < 150
                            ? new Rgb24(20, 150, 255) // Vivid blue sky
                            : new Rgb24(30, 220, 40);  // Vivid green grass
                    }
                }
                img.SaveAsPng(tempFile);
            }

            var result = DocumentDetector.Analyze(tempFile);
            Assert.False(result.IsDocument);
            Assert.True(result.Confidence < 0.40);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void DocumentDetector_PaperDocumentWithText_IsClassifiedAsDocument()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"scan_doc_{Guid.NewGuid():N}.png");
        try
        {
            // A4 ratio 1.414 (e.g. 354 x 500)
            using (var img = new Image<Rgb24>(354, 500))
            {
                // Fill with white/off-white paper
                for (int y = 0; y < 500; y++)
                {
                    for (int x = 0; x < 354; x++)
                    {
                        img[x, y] = new Rgb24(245, 245, 245);
                    }
                }

                // Draw dark horizontal text lines
                for (int row = 40; row < 460; row += 20)
                {
                    for (int y = row; y < row + 6; y++)
                    {
                        for (int x = 40; x < 314; x++)
                        {
                            if ((x % 14) < 10) // simulated words and spacing
                            {
                                img[x, y] = new Rgb24(25, 25, 25);
                            }
                        }
                    }
                }

                img.SaveAsPng(tempFile);
            }

            var result = DocumentDetector.Analyze(tempFile);
            Assert.True(result.IsDocument, System.Text.Json.JsonSerializer.Serialize(result));
            Assert.True(result.Confidence >= 0.65);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void DocumentDetector_IdentityCardWithFace_IsClassifiedAsIdDocument()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"id_card_{Guid.NewGuid():N}.png");
        try
        {
            // ID-1 ratio ~1.586 (e.g. 476 x 300)
            using (var img = new Image<Rgb24>(476, 300))
            {
                // Fill with light background
                for (int y = 0; y < 300; y++)
                {
                    for (int x = 0; x < 476; x++)
                    {
                        img[x, y] = new Rgb24(240, 242, 245);
                    }
                }

                // Text lines
                for (int row = 50; row < 250; row += 25)
                {
                    for (int y = row; y < row + 6; y++)
                    {
                        for (int x = 180; x < 440; x++)
                        {
                            img[x, y] = new Rgb24(20, 20, 20);
                        }
                    }
                }

                img.SaveAsPng(tempFile);
            }

            var result = DocumentDetector.Analyze(tempFile, faceDetected: true, faceCount: 1);
            Assert.True(result.IsDocument, System.Text.Json.JsonSerializer.Serialize(result));
            Assert.True(result.IsIdentityDocument);
            Assert.True(result.Confidence >= 0.80);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void DocumentDetector_SquarePortraitWithFace_IsNotClassifiedAsDocument()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"portrait_{Guid.NewGuid():N}.png");
        try
        {
            using (var img = new Image<Rgb24>(480, 480, new Rgb24(225, 205, 190)))
            {
                for (int y = 45; y < 450; y++)
                for (int x = 75; x < 405; x++)
                {
                    var dx = (x - 240) / 165.0;
                    var dy = (y - 250) / 205.0;
                    if (dx * dx + dy * dy <= 1) img[x, y] = new Rgb24(205, 155, 130);
                }
                for (int y = 45; y < 160; y++)
                for (int x = 100; x < 380; x++) img[x, y] = new Rgb24(45, 35, 35);
                img.SaveAsPng(tempFile);
            }

            var result = DocumentDetector.Analyze(tempFile, faceDetected: true, faceCount: 1);
            Assert.False(result.IsDocument);
            Assert.False(result.IsIdentityDocument);
            Assert.True(result.Confidence <= 0.45);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void DocumentDetector_VideoSubstring_DoesNotCountAsIdKeyword()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"video_portrait_{Guid.NewGuid():N}.png");
        try
        {
            using (var img = new Image<Rgb24>(480, 480, new Rgb24(210, 210, 210))) img.SaveAsPng(tempFile);
            var result = DocumentDetector.Analyze(tempFile, faceDetected: true, faceCount: 1);
            Assert.False(result.IsIdentityDocument);
            Assert.DoesNotContain(result.Reasons, reason => reason.Contains("keyword", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
