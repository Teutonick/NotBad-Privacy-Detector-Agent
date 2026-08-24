using System.Text.Json;
using System.Text.Json.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PrivacyAudit.PeopleDetection;

public sealed class DocumentDetectionResult
{
    [JsonPropertyName("is_document")] public bool IsDocument { get; set; }
    [JsonPropertyName("is_id_document")] public bool IsIdentityDocument { get; set; }
    [JsonPropertyName("confidence")] public double Confidence { get; set; }
    [JsonPropertyName("reasons")] public List<string> Reasons { get; set; } = [];
    [JsonPropertyName("aspect_ratio")] public double AspectRatio { get; set; }
    [JsonPropertyName("text_density")] public double TextDensity { get; set; }
    [JsonPropertyName("scanned_at_utc")] public DateTime ScannedAtUtc { get; set; } = DateTime.UtcNow;

    public static string Serialize(DocumentDetectionResult result) => JsonSerializer.Serialize(result);

    public static bool TryParse(string? json, out DocumentDetectionResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("document_scan", out var prop)) return false;
            result = JsonSerializer.Deserialize<DocumentDetectionResult>(prop.GetRawText());
            return result is not null;
        }
        catch
        {
            return false;
        }
    }

    public static string InjectIntoMetadata(string currentJson, DocumentDetectionResult result)
    {
        try
        {
            var dict = string.IsNullOrWhiteSpace(currentJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson) ?? new();
            dict["document_scan"] = result;
            return JsonSerializer.Serialize(dict);
        }
        catch
        {
            return JsonSerializer.Serialize(new { document_scan = result });
        }
    }
}

public static class DocumentDetector
{
    static readonly HashSet<string> DocumentKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "scan", "doc", "pass", "id", "snils", "inn", "diploma", "certificate", "akt",
        "dogovor", "contract", "чек", "паспорт", "инн", "снилс", "договор", "справка",
        "свидетельство", "квитанция", "билет", "удостоверение", "права", "invoice",
        "receipt", "bill", "statement", "ticket", "license", "agreement", "polis", "полис",
        "document", "passport"
    };

    public static DocumentDetectionResult Analyze(string imagePath, bool faceDetected = false, int faceCount = 0)
    {
        var result = new DocumentDetectionResult();
        if (!File.Exists(imagePath)) return result;

        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<Rgb24>(imagePath);
            var originalWidth = image.Width;
            var originalHeight = image.Height;
            if (originalWidth == 0 || originalHeight == 0) return result;

            double ratio = (double)Math.Max(originalWidth, originalHeight) / Math.Min(originalWidth, originalHeight);
            result.AspectRatio = Math.Round(ratio, 3);

            // Resize to 256x256 for fast uniform analysis
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(256, 256),
                Mode = ResizeMode.Stretch
            }));

            int score = 0;
            var reasons = new List<string>();

            // 1. Color Saturation & Chrominance Analysis (Paper documents have low saturation)
            double totalSaturation = 0;
            int highSatPixels = 0;
            int totalSampled = 0;
            long totalLuminance = 0;
            int darkTextPixels = 0;
            int brightPaperPixels = 0;

            int margin = 20; // Ignore extreme borders
            for (int y = margin; y < 256 - margin; y += 2)
            {
                for (int x = margin; x < 256 - margin; x += 2)
                {
                    var p = image[x, y];
                    byte max = Math.Max(p.R, Math.Max(p.G, p.B));
                    byte min = Math.Min(p.R, Math.Min(p.G, p.B));
                    double sat = max == 0 ? 0 : (double)(max - min) / max;
                    double lum = 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;

                    totalSaturation += sat;
                    totalLuminance += (long)lum;
                    totalSampled++;

                    if (sat > 0.35) highSatPixels++;
                    if (lum < 95) darkTextPixels++;
                    if (lum > 165 && sat < 0.25) brightPaperPixels++;
                }
            }

            double avgSaturation = totalSampled > 0 ? totalSaturation / totalSampled : 1.0;
            double highSatRatio = totalSampled > 0 ? (double)highSatPixels / totalSampled : 1.0;
            double meanLum = totalSampled > 0 ? (double)totalLuminance / totalSampled : 0;
            double darkRatio = totalSampled > 0 ? (double)darkTextPixels / totalSampled : 0;
            double paperRatio = totalSampled > 0 ? (double)brightPaperPixels / totalSampled : 0;

            // Real documents normally contain several separated rows of dark ink over a
            // light neutral surface. A face, hair or clothing can produce many edges, but
            // usually not repeated text-like bands across the image.
            int textBands = 0;
            bool previousBand = false;
            for (int y = margin; y < 256 - margin; y += 2)
            {
                int rowSamples = 0;
                int rowDark = 0;
                int rowPaper = 0;
                for (int x = margin; x < 256 - margin; x += 2)
                {
                    var p = image[x, y];
                    byte max = Math.Max(p.R, Math.Max(p.G, p.B));
                    byte min = Math.Min(p.R, Math.Min(p.G, p.B));
                    double sat = max == 0 ? 0 : (double)(max - min) / max;
                    double lum = 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
                    rowSamples++;
                    if (lum < 95) rowDark++;
                    if (lum > 165 && sat < 0.25) rowPaper++;
                }

                bool currentBand = rowSamples > 0
                    && (double)rowDark / rowSamples is >= 0.04 and <= 0.85
                    && (double)rowPaper / rowSamples >= 0.20;
                if (currentBand && !previousBand) textBands++;
                previousBand = currentBand;
            }

            bool hasStrongPaper = paperRatio >= 0.45 && meanLum is >= 140 and <= 250;
            bool hasModeratePaper = paperRatio >= 0.35 && meanLum is >= 125 and <= 250;

            // Reject highly colorful graphics, 3D renders, landscapes, clothing, wallpapers
            if (avgSaturation > 0.32 || highSatRatio > 0.30)
            {
                result.IsDocument = false;
                result.Confidence = 0.15;
                result.Reasons = ["High color saturation / non-paper content"];
                return result;
            }

            // 2. Paper Background Presence (White / Neutral / Light Gray)
            if (hasStrongPaper)
            {
                score += 30;
                reasons.Add($"Neutral paper/card background ({paperRatio:P0} area, lum {meanLum:0})");
            }
            else if (hasModeratePaper)
            {
                score += 15;
                reasons.Add("Moderate paper background area");
            }

            // 3. Text Contrast & Density Profile (Dark ink on light page)
            bool hasInk = darkRatio is >= 0.04 and <= 0.35 && hasModeratePaper && textBands >= 3;
            if (hasInk)
            {
                score += 25;
                reasons.Add($"Document text ink proportion ({darkRatio:P0})");
            }

            // 4. Horizontal Linearity / Text Row Structure
            int horizontalTransitions = 0;
            int verticalTransitions = 0;
            for (int y = margin; y < 256 - margin; y += 4)
            {
                for (int x = margin; x < 256 - margin - 4; x += 4)
                {
                    var l1 = (image[x, y].R + image[x, y].G + image[x, y].B) / 3;
                    var lRight = (image[x + 4, y].R + image[x + 4, y].G + image[x + 4, y].B) / 3;
                    var lDown = (image[x, y + 4].R + image[x, y + 4].G + image[x, y + 4].B) / 3;

                    if (Math.Abs(l1 - lRight) > 35) horizontalTransitions++;
                    if (Math.Abs(l1 - lDown) > 35) verticalTransitions++;
                }
            }

            double edgeRatio = verticalTransitions > 0 ? (double)horizontalTransitions / verticalTransitions : 1.0;
            bool hasStructuredText = textBands >= 4 && horizontalTransitions + verticalTransitions > 40;
            if (hasStructuredText)
            {
                score += 15;
                reasons.Add($"Repeated document text bands ({textBands})");
            }

            // 5. Standard Document Aspect Ratio
            // ISO 216 / A4 = 1.414, ID-1 (Cards/Licenses) = 1.586, ID-3 (Passport) = 1.42
            bool hasDocumentAspect = Math.Abs(ratio - 1.414) < 0.10 || Math.Abs(ratio - 1.586) < 0.10;
            if (Math.Abs(ratio - 1.414) < 0.10)
            {
                score += 15;
                reasons.Add("Standard A4/A5 document aspect ratio (~1.41)");
            }
            else if (Math.Abs(ratio - 1.586) < 0.10)
            {
                score += 15;
                reasons.Add("ID card / driver license aspect ratio (~1.58)");
            }
            // 6. Filename Keyword Boost
            var fileName = Path.GetFileNameWithoutExtension(imagePath).ToLowerInvariant();
            var fileNameTokens = fileName.Split(['_', '-', ' ', '.', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries);
            bool hasDocumentKeyword = fileNameTokens.Any(DocumentKeywords.Contains);
            if (hasDocumentKeyword)
            {
                score += 15;
                reasons.Add("Document keyword in filename");
            }

            // 7. A face corroborates an already document-like image. It must never turn
            // an ordinary portrait into an identity document by itself.
            bool identityEvidence = faceDetected && faceCount is >= 1 and <= 2
                && hasDocumentAspect && hasStrongPaper && hasInk && hasStructuredText;
            if (identityEvidence)
            {
                score += 20;
                reasons.Add("Face detected on identity document surface");
            }
            else if (faceDetected && faceCount > 0)
            {
                reasons.Add("Face found, but document surface/text/geometry evidence is insufficient");
            }

            result.TextDensity = Math.Round(darkRatio, 3);
            bool hasGeometryEvidence = hasDocumentAspect || (paperRatio >= 0.60 && textBands >= 6) || hasDocumentKeyword;
            result.IsDocument = score >= 65 && hasModeratePaper && hasInk && hasStructuredText && hasGeometryEvidence;
            result.IsIdentityDocument = result.IsDocument && identityEvidence;
            result.Confidence = Math.Round((result.IsDocument ? Math.Min(98, score) : Math.Min(45, score)) / 100.0, 2);
            result.Reasons = reasons;
        }
        catch
        {
            result.IsDocument = false;
            result.Confidence = 0;
        }

        return result;
    }
}
