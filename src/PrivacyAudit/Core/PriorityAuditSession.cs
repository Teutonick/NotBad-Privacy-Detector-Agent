using System.Text.Json;

namespace PrivacyAudit.Core;

public enum PriorityAuditStatus { Ready, Running, Paused, Completed, Canceled }

public sealed class PriorityAuditSession
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string AuditFingerprint { get; set; } = "";
    public PriorityAuditStatus Status { get; set; } = PriorityAuditStatus.Ready;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<Guid> FindingIds { get; set; } = [];
    public List<TriageRouteDecision> Routes { get; set; } = [];
    public HashSet<string> CompletedRoutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SkippedRoutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FailedRoutes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int EligibleFindings { get; set; }
    public int RequestedTenPercent { get; set; }
    public int ConfirmedSignals { get; set; }
    public int Errors { get; set; }
    public int SkippedScanners { get; set; }
    public string CurrentScannerId { get; set; } = "";
    public int CurrentScannerCompleted { get; set; }
    public int CurrentScannerTotal { get; set; }
    public TimeSpan Elapsed { get; set; }
    public int TotalRoutes => Routes.Count;
    public int CompletedRouteCount => CompletedRoutes.Count;
    public double Progress => TotalRoutes == 0 ? 0 : Math.Clamp((double)(CompletedRouteCount + SkippedRoutes.Count) / TotalRoutes, 0, 1);
    public bool HasReport => CompletedRouteCount > 0 || Status == PriorityAuditStatus.Completed;

    public static string RouteKey(Guid findingId, string scannerId) => $"{findingId:N}|{scannerId}";
}

public sealed class PriorityAuditSessionStore(string path)
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    public string Path { get; } = path;

    public PriorityAuditSession? Load()
    {
        if (!File.Exists(Path)) return null;
        try
        {
            var session = JsonSerializer.Deserialize<PriorityAuditSession>(File.ReadAllText(Path), Options);
            if (session is not null)
            {
                session.CompletedRoutes = new(session.CompletedRoutes ?? [], StringComparer.OrdinalIgnoreCase);
                session.SkippedRoutes = new(session.SkippedRoutes ?? [], StringComparer.OrdinalIgnoreCase);
                session.FailedRoutes = new(session.FailedRoutes ?? [], StringComparer.OrdinalIgnoreCase);
            }
            return session;
        }
        catch { return null; }
    }

    public void Save(PriorityAuditSession session)
    {
        session.UpdatedAtUtc = DateTime.UtcNow;
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(session, Options));
        File.Move(temporary, Path, true);
    }

    public void Delete()
    {
        if (File.Exists(Path)) File.Delete(Path);
        if (File.Exists(Path + ".tmp")) File.Delete(Path + ".tmp");
    }
}
