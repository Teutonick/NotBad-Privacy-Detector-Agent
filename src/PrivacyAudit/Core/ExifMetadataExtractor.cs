using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace PrivacyAudit.Core;

public sealed class ExifMetadataResult
{
    [JsonPropertyName("has_geolocation")] public bool HasGeolocation { get; set; }
    [JsonPropertyName("latitude")] public double? Latitude { get; set; }
    [JsonPropertyName("longitude")] public double? Longitude { get; set; }
    [JsonPropertyName("altitude")] public double? Altitude { get; set; }
    [JsonPropertyName("camera_make")] public string? CameraMake { get; set; }
    [JsonPropertyName("camera_model")] public string? CameraModel { get; set; }
    [JsonPropertyName("camera_serial")] public string? CameraSerialNumber { get; set; }
    [JsonPropertyName("lens_model")] public string? LensModel { get; set; }
    [JsonPropertyName("software")] public string? Software { get; set; }
    [JsonPropertyName("author")] public string? Author { get; set; }
    [JsonPropertyName("last_saved_by")] public string? LastSavedBy { get; set; }
    [JsonPropertyName("copyright")] public string? Copyright { get; set; }
    [JsonPropertyName("user_comment")] public string? UserComment { get; set; }
    [JsonPropertyName("date_taken")] public string? DateTaken { get; set; }
    [JsonPropertyName("disclosed_fields")] public List<string> DisclosedFields { get; set; } = [];
    [JsonPropertyName("exposure_level")] public string ExposureLevel { get; set; } = "Low";

    public static string Serialize(ExifMetadataResult result) => JsonSerializer.Serialize(result);

    public static bool TryParse(string? json, out ExifMetadataResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("exif_metadata", out var prop)) return false;
            result = JsonSerializer.Deserialize<ExifMetadataResult>(prop.GetRawText());
            return result is not null;
        }
        catch
        {
            return false;
        }
    }

    public static string InjectIntoMetadata(string currentJson, ExifMetadataResult result)
    {
        try
        {
            var dict = string.IsNullOrWhiteSpace(currentJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson) ?? new();
            dict["exif_metadata"] = result;
            return JsonSerializer.Serialize(dict);
        }
        catch
        {
            return JsonSerializer.Serialize(new { exif_metadata = result });
        }
    }
}

public static class ExifMetadataExtractor
{
    public static ExifMetadataResult Extract(string filePath)
    {
        var result = new ExifMetadataResult();
        if (!File.Exists(filePath)) return result;

        try
        {
            var ext = Path.GetExtension(filePath);
            if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".pptx", StringComparison.OrdinalIgnoreCase))
            {
                ExtractOfficeMetadata(filePath, result);
            }
            else
            {
                ExtractImageExif(filePath, result);
            }

            // Determine overall exposure risk level
            if (result.HasGeolocation)
            {
                result.ExposureLevel = "Critical";
            }
            else if (!string.IsNullOrWhiteSpace(result.CameraSerialNumber) || !string.IsNullOrWhiteSpace(result.Author))
            {
                result.ExposureLevel = "High";
            }
            else if (!string.IsNullOrWhiteSpace(result.CameraModel) || !string.IsNullOrWhiteSpace(result.Software))
            {
                result.ExposureLevel = "Medium";
            }
            else
            {
                result.ExposureLevel = "Low";
            }
        }
        catch
        {
            // Fail gracefully on corrupted image or archive headers
        }

        return result;
    }

    static IExifValue? GetExifValue(ExifProfile exif, ExifTag tag)
    {
        return exif.Values.FirstOrDefault(v => v.Tag == tag);
    }

    static void ExtractImageExif(string imagePath, ExifMetadataResult result)
    {
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(imagePath);
            var exif = info?.Metadata?.ExifProfile;
            if (exif is null || exif.Values.Count == 0) return;

            // 1. GPS Coordinates
            var latValue = GetExifValue(exif, ExifTag.GPSLatitude)?.GetValue() as Rational[];
            var latRef = GetExifValue(exif, ExifTag.GPSLatitudeRef)?.GetValue()?.ToString();
            var lonValue = GetExifValue(exif, ExifTag.GPSLongitude)?.GetValue() as Rational[];
            var lonRef = GetExifValue(exif, ExifTag.GPSLongitudeRef)?.GetValue()?.ToString();

            if (latValue is not null && latValue.Length == 3 && lonValue is not null && lonValue.Length == 3)
            {
                var lat = ConvertToDegrees(latValue);
                var lon = ConvertToDegrees(lonValue);

                if (string.Equals(latRef, "S", StringComparison.OrdinalIgnoreCase)) lat = -lat;
                if (string.Equals(lonRef, "W", StringComparison.OrdinalIgnoreCase)) lon = -lon;

                if (lat is >= -90 and <= 90 && lon is >= -180 and <= 180 && !(lat == 0 && lon == 0))
                {
                    result.Latitude = Math.Round(lat, 6);
                    result.Longitude = Math.Round(lon, 6);
                    result.HasGeolocation = true;
                    result.DisclosedFields.Add("GPS");
                }
            }

            if (GetExifValue(exif, ExifTag.GPSAltitude)?.GetValue() is Rational altRational && altRational.Denominator != 0)
            {
                result.Altitude = Math.Round(altRational.Numerator / (double)altRational.Denominator, 1);
            }

            // 2. Camera Device & Model
            var make = GetExifValue(exif, ExifTag.Make)?.GetValue()?.ToString()?.Trim();
            var model = GetExifValue(exif, ExifTag.Model)?.GetValue()?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(make)) result.CameraMake = make;
            if (!string.IsNullOrWhiteSpace(model))
            {
                result.CameraModel = model.StartsWith(make ?? "", StringComparison.OrdinalIgnoreCase)
                    ? model
                    : $"{make} {model}".Trim();
                result.DisclosedFields.Add("Device");
            }

            // 3. Serial Number & Lens Model
            var lens = GetExifValue(exif, ExifTag.LensModel)?.GetValue()?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(lens)) result.LensModel = lens;

            foreach (var item in exif.Values)
            {
                var tagStr = item.Tag.ToString();
                if (tagStr.Contains("Serial", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(result.CameraSerialNumber))
                {
                    var val = item.GetValue()?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(val) && val.Length >= 4)
                    {
                        result.CameraSerialNumber = val;
                        result.DisclosedFields.Add("Serial");
                    }
                }
            }

            // 4. Software & Editing Tools
            var software = GetExifValue(exif, ExifTag.Software)?.GetValue()?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(software))
            {
                result.Software = software;
                result.DisclosedFields.Add("Software");
            }

            // 5. Author, Artist & Copyright
            var author = GetExifValue(exif, ExifTag.Artist)?.GetValue()?.ToString()?.Trim()
                ?? GetExifValue(exif, ExifTag.XPAuthor)?.GetValue()?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(author))
            {
                result.Author = author;
                result.DisclosedFields.Add("Author");
            }

            var copyright = GetExifValue(exif, ExifTag.Copyright)?.GetValue()?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(copyright)) result.Copyright = copyright;

            // 6. User Comment / Notes
            var comment = GetExifValue(exif, ExifTag.UserComment)?.GetValue()?.ToString()?.Trim()
                ?? GetExifValue(exif, ExifTag.XPComment)?.GetValue()?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(comment) && !comment.StartsWith("UNICODE", StringComparison.OrdinalIgnoreCase))
            {
                result.UserComment = comment;
            }

            // 7. Date Taken
            var date = GetExifValue(exif, ExifTag.DateTimeOriginal)?.GetValue()?.ToString()?.Trim()
                ?? GetExifValue(exif, ExifTag.DateTimeDigitized)?.GetValue()?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(date))
            {
                result.DateTaken = date;
                result.DisclosedFields.Add("DateTaken");
            }
        }
        catch
        {
            // Fail safely on corrupted EXIF segments
        }
    }

    static void ExtractOfficeMetadata(string docPath, ExifMetadataResult result)
    {
        try
        {
            using var stream = new FileStream(docPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var coreEntry = zip.GetEntry("docProps/core.xml");
            if (coreEntry is null) return;

            using var entryStream = coreEntry.Open();
            using var reader = XmlReader.Create(entryStream, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;

                if (reader.LocalName == "creator")
                {
                    var author = reader.ReadElementContentAsString().Trim();
                    if (!string.IsNullOrWhiteSpace(author))
                    {
                        result.Author = author;
                        result.DisclosedFields.Add("Author");
                    }
                }
                else if (reader.LocalName == "lastModifiedBy")
                {
                    var last = reader.ReadElementContentAsString().Trim();
                    if (!string.IsNullOrWhiteSpace(last))
                    {
                        result.LastSavedBy = last;
                        result.DisclosedFields.Add("LastSavedBy");
                    }
                }
                else if (reader.LocalName == "created")
                {
                    var created = reader.ReadElementContentAsString().Trim();
                    if (!string.IsNullOrWhiteSpace(created))
                    {
                        result.DateTaken = created;
                        result.DisclosedFields.Add("DateTaken");
                    }
                }
            }
        }
        catch
        {
            // Fail safely on unreadable Office archives
        }
    }

    static double ConvertToDegrees(Rational[] rationals)
    {
        double degrees = rationals[0].Denominator == 0 ? 0 : rationals[0].Numerator / (double)rationals[0].Denominator;
        double minutes = rationals[1].Denominator == 0 ? 0 : rationals[1].Numerator / (double)rationals[1].Denominator;
        double seconds = rationals[2].Denominator == 0 ? 0 : rationals[2].Numerator / (double)rationals[2].Denominator;

        return degrees + (minutes / 60.0) + (seconds / 3600.0);
    }
}
