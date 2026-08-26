using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrivacyAudit.PeopleDetection;

public enum ImageSafetyScanStatus { Completed, Error }
public enum ImageSafetyClass { NSFL, NSFW, SFW }

public sealed record ImageSafetyScanResult(
    string Path, ImageSafetyScanStatus Status, ImageSafetyClass PrimaryClass,
    double NsflScore, double NsfwScore, double SfwScore, string ModelVersion,
    DateTime ScannedAtUtc, long FileSize, DateTime FileModifiedAt, string Error = "")
{
    public bool IsReusable(string path, long size, DateTime modifiedAt, string modelVersion) =>
        Status == ImageSafetyScanStatus.Completed &&
        string.Equals(Path, path, StringComparison.OrdinalIgnoreCase) && FileSize == size &&
        FileModifiedAt == modifiedAt && string.Equals(ModelVersion, modelVersion, StringComparison.Ordinal);
}

public sealed record ImageSafetyScanProgress(string CurrentPath, int Completed, int Total, int Nsfw, int Nsfl, int Errors, string Message = "");

public static class ImageSafetyMetadata
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static string InjectIntoMetadata(string? currentJson, ImageSafetyScanResult result)
    {
        try
        {
            var metadata = string.IsNullOrWhiteSpace(currentJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson) ?? new();
            metadata["image_safety"] = new MetadataDocument(result);
            return JsonSerializer.Serialize(metadata);
        }
        catch { return JsonSerializer.Serialize(new Dictionary<string, object> { ["image_safety"] = new MetadataDocument(result) }); }
    }

    public static bool TryParse(string? json, out ImageSafetyScanResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var root = JsonDocument.Parse(json);
            if (!root.RootElement.TryGetProperty("image_safety", out var node)) return false;
            var value = node.Deserialize<MetadataDocument>(Options);
            if (value is null || !Enum.TryParse(value.Status, true, out ImageSafetyScanStatus status) ||
                !Enum.TryParse(value.PrimaryClass, true, out ImageSafetyClass primaryClass)) return false;
            result = new("", status, primaryClass, value.NsflScore, value.NsfwScore, value.SfwScore,
                value.ModelVersion, value.ScannedAtUtc, value.FileSize, value.FileModifiedAt, value.Error ?? "");
            return true;
        }
        catch (JsonException) { return false; }
    }

    public const double NsfwFilterThreshold = 0.85;
    public static bool IsHighConfidenceNsfw(string? json) =>
        TryParse(json, out var result) && result!.Status == ImageSafetyScanStatus.Completed && result.NsfwScore > NsfwFilterThreshold;

    sealed class MetadataDocument
    {
        public MetadataDocument() { }
        public MetadataDocument(ImageSafetyScanResult result)
        {
            Status = result.Status.ToString(); PrimaryClass = result.PrimaryClass.ToString();
            NsflScore = result.NsflScore; NsfwScore = result.NsfwScore; SfwScore = result.SfwScore;
            ModelVersion = result.ModelVersion; ScannedAtUtc = result.ScannedAtUtc;
            FileSize = result.FileSize; FileModifiedAt = result.FileModifiedAt; Error = result.Error;
        }
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("primary_class")] public string PrimaryClass { get; set; } = "";
        [JsonPropertyName("nsfl_score")] public double NsflScore { get; set; }
        [JsonPropertyName("nsfw_score")] public double NsfwScore { get; set; }
        [JsonPropertyName("sfw_score")] public double SfwScore { get; set; }
        [JsonPropertyName("model_version")] public string ModelVersion { get; set; } = "";
        [JsonPropertyName("scanned_at_utc")] public DateTime ScannedAtUtc { get; set; }
        [JsonPropertyName("file_size")] public long FileSize { get; set; }
        [JsonPropertyName("file_modified_at")] public DateTime FileModifiedAt { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}
