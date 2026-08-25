using PrivacyAudit.PeopleDetection;

namespace PrivacyAudit.Core;

public static class PrivacyRadarRanking
{
    public static int ObjectiveRisk(Finding finding)
    {
        var score = finding.ExposureScore;
        if (PiiDetectionResult.TryParse(finding.MetadataJson, out var pii) && pii!.TotalMatches > 0) score += 18;
        if (SecretDetectionResult.TryParse(finding.MetadataJson, out var secrets) && secrets!.TotalMatches > 0) score += 28;
        if (CredentialConfigResult.TryParse(finding.MetadataJson, out var config) && config!.IsCredentialConfig) score += 22;
        if (IdentityTraceResult.TryParse(finding.MetadataJson, out var identity) && identity!.HasIdentityTrace) score += 14;
        if (ArchiveInspectionResult.TryParse(finding.MetadataJson, out var archive) && archive!.SensitiveEntriesCount > 0) score += 24;
        if (DocumentDetectionResult.TryParse(finding.MetadataJson, out var document) && document!.IsDocument) score += document.IsIdentityDocument ? 24 : 15;
        if (PeopleScanMetadata.TryParse(finding.MetadataJson, out var people) && people!.PeopleDetected) score += 10;
        if (ExifMetadataResult.TryParse(finding.MetadataJson, out var exif))
        {
            if (exif!.DisclosedFields.Count > 0) score += 6;
            if (exif.HasGeolocation) score += 16;
        }
        return Math.Clamp(score, 0, 100);
    }

    public static int Score(Finding finding)
    {
        var objective = ObjectiveRisk(finding);
        if (finding.PersonalAttentionScore is not float personal) return objective;
        var combined = (int)Math.Round((objective * 0.80) + (personal * 0.20));
        return Math.Max(CriticalEvidenceFloor(finding), Math.Clamp(combined, 0, 100));
    }

    static int CriticalEvidenceFloor(Finding finding)
    {
        if (SecretDetectionResult.TryParse(finding.MetadataJson, out var secret) && secret!.TotalMatches > 0) return 90;
        if (CredentialConfigResult.TryParse(finding.MetadataJson, out var config) && config!.IsCredentialConfig) return 85;
        if (DocumentDetectionResult.TryParse(finding.MetadataJson, out var document) && document!.IsIdentityDocument) return 80;
        if (ExifMetadataResult.TryParse(finding.MetadataJson, out var exif) && exif!.HasGeolocation) return 75;
        return 0;
    }

    public static int ConfirmedSignals(Finding finding)
    {
        var signals = 0;
        if (PiiDetectionResult.TryParse(finding.MetadataJson, out var pii) && pii!.TotalMatches > 0) signals++;
        if (SecretDetectionResult.TryParse(finding.MetadataJson, out var secrets) && secrets!.TotalMatches > 0) signals++;
        if (CredentialConfigResult.TryParse(finding.MetadataJson, out var config) && config!.IsCredentialConfig) signals++;
        if (IdentityTraceResult.TryParse(finding.MetadataJson, out var identity) && identity!.HasIdentityTrace) signals++;
        if (ArchiveInspectionResult.TryParse(finding.MetadataJson, out var archive) && archive!.SensitiveEntriesCount > 0) signals++;
        if (DocumentDetectionResult.TryParse(finding.MetadataJson, out var document) && document!.IsDocument) signals++;
        if (PeopleScanMetadata.TryParse(finding.MetadataJson, out var people) && people!.PeopleDetected) signals++;
        if (ExifMetadataResult.TryParse(finding.MetadataJson, out var exif) && exif!.DisclosedFields.Count > 0) signals++;
        return signals;
    }
}
