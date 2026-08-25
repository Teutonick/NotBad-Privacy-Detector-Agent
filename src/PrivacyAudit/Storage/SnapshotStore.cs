using System.Text.Json;
using System.Text;
using PrivacyAudit.Core;

namespace PrivacyAudit.Storage;

public sealed record AuditSnapshotContext(
    ScanPreset Preset,
    List<string> Roots,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    TimeSpan Duration);

public sealed record ScanSnapshot(DateTime SavedAtUtc, List<Finding> Findings, AuditSnapshotContext? Context = null);

public static class SnapshotStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false, PropertyNameCaseInsensitive = true };

    public static string PathFor(string appDataDirectory) => System.IO.Path.Combine(appDataDirectory, "last-scan.json");

    public static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        var temporary = path + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
    }

    public static void Save(string path, DateTime savedAtUtc, IEnumerable<Finding> findings, AuditSnapshotContext? context = null)
    {
        var materialized = findings.Take(StorageLimits.MaxSnapshotFindings + 1).ToList();
        if (materialized.Count > StorageLimits.MaxSnapshotFindings)
            throw new InvalidOperationException($"Snapshot exceeds the {StorageLimits.MaxSnapshotFindings:N0}-finding safety limit.");
        var document = new SnapshotDocument
        {
            SavedAtUtc = savedAtUtc,
            Context = context,
            Findings = materialized.Select(ToSnapshot).ToList()
        };
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        var json = JsonSerializer.Serialize(document, Options);
        if (Encoding.UTF8.GetByteCount(json) > StorageLimits.MaxSnapshotBytes)
            throw new InvalidOperationException($"Snapshot exceeds the {StorageLimits.MaxSnapshotBytes / (1024 * 1024)} MB safety limit.");
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, true);
    }

    public static ScanSnapshot? Load(string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        progress?.Report("Reading the saved audit…");
        var document = JsonSerializer.Deserialize<SnapshotDocument>(File.ReadAllText(path), Options);
        cancellationToken.ThrowIfCancellationRequested();
        if (document is null) return null;
        progress?.Report("Restoring finding metadata…");
        var findings = document.Findings.Select(FromSnapshot).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        return new(document.SavedAtUtc, findings, document.Context);
    }

    public static Task<ScanSnapshot?> LoadAsync(string path, IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(path, progress, cancellationToken), cancellationToken);

    static SnapshotFinding ToSnapshot(Finding f) => new()
    {
        Id = f.Id, ScannerId = f.ScannerId, Category = f.Category, Subcategory = f.Subcategory,
        Path = f.Path, DisplayName = f.DisplayName, SizeBytes = f.SizeBytes, CreatedAt = f.CreatedAt,
        ModifiedAt = f.ModifiedAt, LastAccessAt = f.LastAccessAt, ExposureScore = f.ExposureScore,
        ExposureReasons = f.ExposureReasons.ToList(), AgeClass = f.AgeClass, MetadataJson = f.MetadataJson, Ignored = f.Ignored
    };

    static Finding FromSnapshot(SnapshotFinding f) => new()
    {
        Id = f.Id, ScannerId = f.ScannerId ?? "", Category = f.Category ?? "Other", Subcategory = f.Subcategory ?? "",
        Path = f.Path ?? "", DisplayName = f.DisplayName ?? "", SizeBytes = f.SizeBytes, CreatedAt = f.CreatedAt,
        ModifiedAt = f.ModifiedAt, LastAccessAt = f.LastAccessAt, ExposureScore = f.ExposureScore,
        ExposureReasons = f.ExposureReasons ?? [], MetadataJson = f.MetadataJson ?? "{}", Ignored = f.Ignored
    };

    sealed class SnapshotDocument
    {
        public DateTime SavedAtUtc { get; set; }
        public AuditSnapshotContext? Context { get; set; }
        public List<SnapshotFinding> Findings { get; set; } = [];
    }

    sealed class SnapshotFinding
    {
        public Guid Id { get; set; }
        public string? ScannerId { get; set; }
        public string? Category { get; set; }
        public string? Subcategory { get; set; }
        public string? Path { get; set; }
        public string? DisplayName { get; set; }
        public long SizeBytes { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public DateTime? LastAccessAt { get; set; }
        public int ExposureScore { get; set; }
        public List<string>? ExposureReasons { get; set; }
        public string? AgeClass { get; set; }
        public string? MetadataJson { get; set; }
        public bool Ignored { get; set; }
    }
}
