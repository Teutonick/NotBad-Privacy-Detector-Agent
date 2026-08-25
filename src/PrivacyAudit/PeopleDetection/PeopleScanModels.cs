using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrivacyAudit.PeopleDetection;

public enum PeopleScanStatus { Completed, Error }

public sealed record PeopleScanResult(
    string Path,
    PeopleScanStatus Status,
    bool PeopleDetected,
    int FaceCount,
    double MaxConfidence,
    string ModelVersion,
    DateTime ScannedAtUtc,
    long FileSize,
    DateTime FileModifiedAt,
    string Error = "")
{
    public bool IsReusable(string path, long size, DateTime modifiedAt, string modelVersion) =>
        Status == PeopleScanStatus.Completed &&
        string.Equals(Path, path, StringComparison.OrdinalIgnoreCase) &&
        FileSize == size && FileModifiedAt == modifiedAt && string.Equals(ModelVersion, modelVersion, StringComparison.Ordinal);
}

public sealed record PeopleScanProgress(string CurrentPath, int Completed, int Total, int People, int Errors, string Message = "");

public static class PeopleScanMetadata
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static string Serialize(PeopleScanResult result) => JsonSerializer.Serialize(new MetadataDocument
    {
        PeopleScanStatus = result.Status.ToString(), PeopleDetected = result.PeopleDetected, FaceCount = result.FaceCount,
        MaxConfidence = result.MaxConfidence, ModelVersion = result.ModelVersion, ScannedAt = result.ScannedAtUtc,
        FileSize = result.FileSize, FileModifiedAt = result.FileModifiedAt, Error = result.Error
    });

    public static string InjectIntoMetadata(string? currentJson, PeopleScanResult result)
    {
        try
        {
            var metadata = string.IsNullOrWhiteSpace(currentJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson) ?? new();
            var people = JsonSerializer.Deserialize<Dictionary<string, object>>(Serialize(result)) ?? new();
            foreach (var item in people) metadata[item.Key] = item.Value;
            return JsonSerializer.Serialize(metadata);
        }
        catch
        {
            return Serialize(result);
        }
    }

    public static bool TryParse(string? json, out PeopleScanResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            var value = JsonSerializer.Deserialize<MetadataDocument>(json, Options);
            if (value is null || string.IsNullOrWhiteSpace(value.ModelVersion)) return false;
            if (!Enum.TryParse<PeopleScanStatus>(value.PeopleScanStatus, true, out var status)) return false;
            result = new("", status, value.PeopleDetected, value.FaceCount, value.MaxConfidence, value.ModelVersion, value.ScannedAt, value.FileSize, value.FileModifiedAt, value.Error ?? "");
            return true;
        }
        catch (JsonException) { return false; }
    }

    sealed class MetadataDocument
    {
        [JsonPropertyName("people_scan_status")] public string PeopleScanStatus { get; set; } = "";
        [JsonPropertyName("people_detected")] public bool PeopleDetected { get; set; }
        [JsonPropertyName("face_count")] public int FaceCount { get; set; }
        [JsonPropertyName("max_confidence")] public double MaxConfidence { get; set; }
        [JsonPropertyName("model_version")] public string ModelVersion { get; set; } = "";
        [JsonPropertyName("scanned_at")] public DateTime ScannedAt { get; set; }
        [JsonPropertyName("file_size")] public long FileSize { get; set; }
        [JsonPropertyName("file_modified_at")] public DateTime FileModifiedAt { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}
