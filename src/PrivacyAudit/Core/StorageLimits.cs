namespace PrivacyAudit.Core;

/// <summary>Hard local retention limits preventing PrivacyAudit from becoming a source of storage bloat.</summary>
public static class StorageLimits
{
    public static readonly TimeSpan AuditRetention = TimeSpan.FromDays(183);
    public const long MaxDiagnosticLogBytes = 10L * 1024 * 1024;
    public const long MaxSnapshotBytes = 256L * 1024 * 1024;
    // A deliberately very high count ceiling for large disks; the byte ceiling remains the practical guard.
    public const int MaxSnapshotFindings = 1_000_000_000;
    public const int MaxPersonalFeedbackRows = 100_000;
    public const long MaxSmallMetadataBytes = 256L * 1024;

    public static void TrimTextLog(string path, long maximumBytes)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= maximumBytes) return;
            var bytes = File.ReadAllBytes(path);
            var keep = bytes.AsSpan(Math.Max(0, bytes.Length - (int)maximumBytes)).ToArray();
            File.WriteAllBytes(path, keep);
        }
        catch { /* Retention must never stop an audit or error report. */ }
    }
}
