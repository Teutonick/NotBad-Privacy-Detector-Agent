using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using PrivacyAudit.Core;
using PrivacyAudit.PeopleDetection;

namespace PrivacyAudit;

public sealed class NsfwBlurEffectConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not true || values[0] is not string json) return null;
        return ImageSafetyMetadata.TryParse(json, out var result) && result!.Status == ImageSafetyScanStatus.Completed && result.PrimaryClass == ImageSafetyClass.NSFW
            ? new System.Windows.Media.Effects.BlurEffect { Radius = 24 }
            : null;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class LocalizedBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? LocalizationService.Get("Yes") : LocalizationService.Get("No");
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

public sealed class ApplicationHistoryRiskConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RiskLevel.High or RiskLevel.Critical => LocalizationService.Get("ApplicationHistoryImportantFinding"),
        RiskLevel.Medium => LocalizationService.Get("ApplicationHistoryNeedsAttention"),
        _ => LocalizationService.Get("ApplicationHistoryNoFinding")
    };
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

public sealed class ThumbnailConverter : IValueConverter
{
    public int DecodePixelWidth { get; set; } = 64;
    public object? Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path) || !string.Equals(Classifier.File(path), "Images", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit(); image.UriSource = new Uri(path); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = DecodePixelWidth; image.EndInit(); image.Freeze();
            return image;
        }
        catch { return null; }
    }
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

public sealed class FileGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Images" => "▧",
            "Video" => "▶",
            "Audio" => "♫",
            "Documents" => "▤",
            "Archives" => "□",
            "Potential secrets" => "◇",
            "AI / Models" => "✦",
            "Development" => "⌘",
            _ => "•"
        };
    }
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

public sealed class FindingBadgeConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string json || string.IsNullOrWhiteSpace(json)) return LocalizationService.Get("DetectionUnknown");
        var badges = new List<string>();

        if (PiiDetectionResult.TryParse(json, out var pii) && pii!.TotalMatches > 0)
        {
            var label = LocalizationService.Get("PiiBadgePrefix");
            badges.Add($"{label}: {pii.TotalMatches}");
        }

        if (SecretDetectionResult.TryParse(json, out var sec) && sec!.TotalMatches > 0)
        {
            var label = LocalizationService.Get("SecretBadgePrefix");
            badges.Add($"{label}: {sec.TotalMatches}");
        }

        if (CredentialConfigResult.TryParse(json, out var cfg) && cfg!.IsCredentialConfig)
        {
            var label = LocalizationService.Get("ConfigBadgePrefix");
            badges.Add($"{label}: {cfg.ExposureLevel}");
        }

        if (IdentityTraceResult.TryParse(json, out var idt) && idt!.HasIdentityTrace)
        {
            var label = LocalizationService.Get("IdentityBadgePrefix");
            badges.Add($"{label}: {idt.TotalMentions}");
        }

        if (ArchiveInspectionResult.TryParse(json, out var arch) && arch!.IsArchive && arch.SensitiveEntriesCount > 0)
        {
            var label = LocalizationService.Get("ArchiveBadgePrefix");
            badges.Add($"{label}: {arch.PrivacyScore} ({arch.SensitiveEntriesCount})");
        }

        if (ExifMetadataResult.TryParse(json, out var exif))
        {
            if (exif!.HasGeolocation)
            {
                badges.Add($"GPS: {exif.Latitude:F2}, {exif.Longitude:F2}");
            }
            else if (!string.IsNullOrWhiteSpace(exif.CameraModel))
            {
                badges.Add($"EXIF: {exif.CameraModel}");
            }
        }

        if (DocumentDetectionResult.TryParse(json, out var document) && document!.IsDocument)
        {
            var label = LocalizationService.Get(document.IsIdentityDocument ? "IdDocumentBadge" : "DocumentBadge");
            badges.Add(string.Format(label, document.Confidence));
        }

        if (PeopleScanMetadata.TryParse(json, out var people) && people!.PeopleDetected)
            badges.Add($"{LocalizationService.Get("PeopleDetected")}: {people.FaceCount}");

        if (ImageSafetyMetadata.TryParse(json, out var safety) && safety!.Status == ImageSafetyScanStatus.Completed && safety.PrimaryClass != ImageSafetyClass.SFW)
            badges.Add($"{safety.PrimaryClass} · NSFW Score: {safety.NsfwScore:P0}");

        if (badges.Count > 0) return string.Join("  •  ", badges);
        return DetectionEvidenceCalculator.Summarize(json).HasCompletedScan
            ? LocalizationService.Get("DetectionNoFindings")
            : LocalizationService.Get("DetectionUnknown");
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

public sealed class PeopleBadgeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string json) return LocalizationService.Get("PeopleNotScanned");

        // 1. Check if Document analysis was performed
        if (DocumentDetectionResult.TryParse(json, out var doc) && doc!.IsDocument)
        {
            var label = doc.IsIdentityDocument
                ? LocalizationService.Get("IdDocumentBadge")
                : LocalizationService.Get("DocumentBadge");
            return string.Format(label, doc.Confidence);
        }

        // 2. Check if People scan was performed
        if (PeopleScanMetadata.TryParse(json, out var result))
        {
            if (result!.Status == PeopleScanStatus.Error) return LocalizationService.Get("PeopleScanErrors");
            if (result.Status == PeopleScanStatus.Completed)
            {
                return result.PeopleDetected
                    ? string.Format(LocalizationService.Get("PeopleTileDetected"), result.FaceCount, result.MaxConfidence)
                    : LocalizationService.Get("NoPeopleDetected");
            }
        }

        // 3. Check if GPS / EXIF metadata was analyzed
        if (ExifMetadataResult.TryParse(json, out var exif))
        {
            if (exif!.HasGeolocation)
            {
                return $"GPS · {exif.Latitude:F2}, {exif.Longitude:F2}";
            }
            if (!string.IsNullOrWhiteSpace(exif.CameraModel))
            {
                return $"EXIF · {exif.CameraModel}";
            }
        }

        return LocalizationService.Get("PeopleNotScanned");
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

public sealed class FeedbackOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var targetTag = parameter?.ToString();
        var label = value as bool?;

        if (targetTag == "True")
        {
            return label == true ? 1.0 : 0.45;
        }
        if (targetTag == "False")
        {
            return label == false ? 1.0 : 0.45;
        }
        if (targetTag == "Clear")
        {
            return label != null ? 0.8 : 0.25;
        }
        return 0.45;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}

public sealed class FeedbackBackgroundConverter : IValueConverter
{
    static readonly System.Windows.Media.SolidColorBrush PositiveBrush = new(System.Windows.Media.Color.FromArgb(0x35, 0x30, 0xD1, 0x58));
    static readonly System.Windows.Media.SolidColorBrush NegativeBrush = new(System.Windows.Media.Color.FromArgb(0x35, 0xFF, 0x45, 0x3A));
    static readonly System.Windows.Media.SolidColorBrush DefaultBrush = new(System.Windows.Media.Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));

    static FeedbackBackgroundConverter()
    {
        PositiveBrush.Freeze();
        NegativeBrush.Freeze();
        DefaultBrush.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var targetTag = parameter?.ToString();
        var label = value as bool?;

        if (targetTag == "True" && label == true)
        {
            return PositiveBrush;
        }
        if (targetTag == "False" && label == false)
        {
            return NegativeBrush;
        }
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
