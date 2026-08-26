using System.Text.Json;
using System.Text.Json.Nodes;
using PrivacyAudit.PeopleDetection;

namespace PrivacyAudit.Core;

public sealed record DetectionEvidenceSummary(
    bool HasCompletedScan,
    int ConfirmedCategoryCount,
    int EvidenceCount)
{
    public bool HasConfirmedDetections => ConfirmedCategoryCount > 0;
}

public static class DetectionEvidenceCalculator
{
    public const string Pii = "pii";
    public const string Secrets = "secrets";
    public const string Configs = "configs";
    public const string Identity = "identity";
    public const string Archives = "archives";
    public const string Documents = "documents";
    public const string People = "people";
    public const string ImageSafety = "image_safety";
    public const string Exif = "exif";

    public static string MarkCompleted(string? currentJson, string scannerKey)
    {
        try
        {
            var root = string.IsNullOrWhiteSpace(currentJson)
                ? new JsonObject()
                : JsonNode.Parse(currentJson) as JsonObject ?? new JsonObject();
            var status = root["scan_status"] as JsonObject ?? new JsonObject();
            status[scannerKey] = true;
            root["scan_status"] = status;
            return root.ToJsonString();
        }
        catch (JsonException)
        {
            return currentJson ?? "{}";
        }
    }

    public static DetectionEvidenceSummary Summarize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(false, 0, 0);

        var completed = HasCompletedStatus(json, Pii) || HasCompletedStatus(json, Secrets) || HasCompletedStatus(json, Configs) || HasCompletedStatus(json, Identity) || HasCompletedStatus(json, Archives) || HasCompletedStatus(json, Documents) || HasCompletedStatus(json, People) || HasCompletedStatus(json, ImageSafety) || HasCompletedStatus(json, Exif);
        var categories = 0;
        var evidence = 0;

        if (PiiDetectionResult.TryParse(json, out var pii) && pii!.TotalMatches > 0) { categories++; evidence += pii.TotalMatches; }
        if (SecretDetectionResult.TryParse(json, out var secrets) && secrets!.TotalMatches > 0) { categories++; evidence += secrets.TotalMatches; }
        if (CredentialConfigResult.TryParse(json, out var configs) && configs!.IsCredentialConfig) { categories++; evidence++; }
        if (IdentityTraceResult.TryParse(json, out var identity) && identity!.HasIdentityTrace) { categories++; evidence += Math.Max(1, identity.TotalMentions); }
        if (ArchiveInspectionResult.TryParse(json, out var archive) && archive!.IsArchive && archive.SensitiveEntriesCount > 0) { categories++; evidence += archive.SensitiveEntriesCount; }
        if (DocumentDetectionResult.TryParse(json, out var document) && document!.IsDocument) { categories++; evidence += document.IsIdentityDocument ? 2 : 1; }
        if (PeopleScanMetadata.TryParse(json, out var people) && people!.PeopleDetected) { categories++; evidence += Math.Max(1, people.FaceCount); }
        if (ImageSafetyMetadata.TryParse(json, out var safety) && safety!.Status == ImageSafetyScanStatus.Completed && safety.PrimaryClass != ImageSafetyClass.SFW) { categories++; evidence++; }
        if (ExifMetadataResult.TryParse(json, out var exif) && exif!.DisclosedFields.Count > 0) { categories++; evidence += exif.DisclosedFields.Count; }

        return new(completed, categories, evidence);
    }

    public static long PriorityRank(Finding finding)
    {
        var summary = Summarize(finding.MetadataJson);
        if (summary.HasConfirmedDetections)
            return 2_000_000_000L + summary.EvidenceCount * 10_000L + summary.ConfirmedCategoryCount * 100L + finding.PrivacyRiskRank;
        if (summary.HasCompletedScan)
            return 1_000_000_000L + finding.PrivacyRiskRank;
        return finding.PrivacyRiskRank;
    }

    public static bool IsCompleted(string? json, string scannerKey) => !string.IsNullOrWhiteSpace(json) && HasCompletedStatus(json, scannerKey);

    static bool HasCompletedStatus(string json, string scannerKey)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("scan_status", out var status)
                && status.ValueKind == JsonValueKind.Object
                && status.TryGetProperty(scannerKey, out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
