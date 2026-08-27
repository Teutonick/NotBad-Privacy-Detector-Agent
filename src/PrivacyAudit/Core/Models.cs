using System.Collections.ObjectModel;

namespace PrivacyAudit.Core;

public enum RiskLevel { None, Low, Medium, High, Critical }
public enum ScanPreset { Quick, Full, Custom }

public sealed class Finding : System.ComponentModel.INotifyPropertyChanged
{
    bool? _personalAttentionLabel;
    float? _personalAttentionScore;
    string _applicationHistoryReferences = "";
    DateTime? _applicationHistoryLastSeen;
    int _applicationHistoryInteractionCount;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ScannerId { get; init; } = "";
    public string Category { get; set; } = "Other";
    public string Subcategory { get; set; } = "";
    public string Path { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? ModifiedAt { get; init; }
    public DateTime? LastAccessAt { get; init; }
    public int ExposureScore { get; init; }
    public IReadOnlyList<string> ExposureReasons { get; init; } = [];
    public RiskLevel RiskLevel => ExposureCalculator.Level(ExposureScore);
    public string AgeClass => Classifier.Age(ModifiedAt);
    public string MetadataJson { get; set; } = "{}";
    public bool Ignored { get; set; }
    public string SizeDisplay => Format.Bytes(SizeBytes);
    public string ReasonDisplay => string.Join("; ", ExposureReasons);
    public string ApplicationHistoryReferences { get => _applicationHistoryReferences; set { if (_applicationHistoryReferences == value) return; _applicationHistoryReferences = value; OnChanged(); } }
    public DateTime? ApplicationHistoryLastSeen { get => _applicationHistoryLastSeen; set { if (_applicationHistoryLastSeen == value) return; _applicationHistoryLastSeen = value; OnChanged(); } }
    public int ApplicationHistoryInteractionCount { get => _applicationHistoryInteractionCount; set { if (_applicationHistoryInteractionCount == value) return; _applicationHistoryInteractionCount = value; OnChanged(); } }
    public bool? PersonalAttentionLabel { get => _personalAttentionLabel; set { if (_personalAttentionLabel == value) return; _personalAttentionLabel = value; OnChanged(); OnChanged(nameof(PersonalFeedbackDisplay)); } }
    public float? PersonalAttentionScore { get => _personalAttentionScore; set { if (_personalAttentionScore == value) return; _personalAttentionScore = value; OnChanged(); OnChanged(nameof(PersonalAttentionDisplay)); } }
    public string PersonalFeedbackDisplay => PersonalAttentionLabel switch { true => "👍", false => "👎", _ => "—" };
    public string PersonalAttentionDisplay => PersonalAttentionScore is float score ? $"{score:0}%" : "—";
    public int ObjectivePrivacyRisk => PrivacyRadarRanking.ObjectiveRisk(this);
    public int CombinedPriority => PrivacyRadarRanking.Score(this);
    public int PrivacyRiskRank => (int)Math.Round((ObjectivePrivacyRisk + CombinedPriority) / 2d, MidpointRounding.AwayFromZero);
    public long DetectionPriorityRank => DetectionEvidenceCalculator.PriorityRank(this);
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    void OnChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed record ScanProgress(string Scanner, string CurrentPath, long Files, long Bytes, long Findings, string Message = "");
public sealed record ScannerResult(string ScannerId, IReadOnlyList<Finding> Findings, int Warnings, int Errors, TimeSpan Duration);
public sealed class ScanContext
{
    public required ScanPreset Preset { get; init; }
    public required IReadOnlyList<string> Roots { get; init; }
    public required IReadOnlyList<string> Exclusions { get; init; }
    public required IProgress<ScanProgress> Progress { get; init; }
    public long LargeFileThreshold { get; init; } = 1L << 30;
    public bool IsExcluded(string path) => Exclusions.Any(x => path.StartsWith(x, StringComparison.OrdinalIgnoreCase));
}

public interface IPrivacyScanner
{
    string Id { get; }
    string Name { get; }
    Task<ScannerResult> ScanAsync(ScanContext context, CancellationToken cancellationToken);
}

public static class ExposureCalculator
{
    public static int Calculate(IEnumerable<int> factors) => Math.Min(100, factors.Sum());
    public static RiskLevel Level(int score) => score switch { >= 80 => RiskLevel.Critical, >= 60 => RiskLevel.High, >= 30 => RiskLevel.Medium, >= 1 => RiskLevel.Low, _ => RiskLevel.None };
}

public static class ScanPresetPolicy
{
    public static bool IncludesSystemScanners(ScanPreset preset) => preset != ScanPreset.Quick;
}

public static class Classifier
{
    static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".avif" };
    static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".avi", ".mkv", ".webm", ".mts", ".m2ts" };
    static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".flac", ".aac", ".ogg", ".m4a" };
    static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2" };
    static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf" };
    static readonly HashSet<string> Models = new(StringComparer.OrdinalIgnoreCase) { ".safetensors", ".ckpt", ".pth", ".pt", ".gguf", ".onnx" };
    public static string File(string path) { var e = System.IO.Path.GetExtension(path); return Images.Contains(e) ? "Images" : Videos.Contains(e) ? "Video" : Audio.Contains(e) ? "Audio" : Archives.Contains(e) ? "Archives" : Documents.Contains(e) ? "Documents" : Models.Contains(e) ? "AI / Models" : "Other"; }
    public static string Age(DateTime? dt) { if (dt is null) return "Unknown"; var m = (DateTime.Now - dt.Value).TotalDays / 30.4375; return m < 6 ? "< 6 months" : m < 12 ? "6–12 months" : m < 24 ? "1–2 years" : m < 36 ? "2–3 years" : m < 60 ? "3–5 years" : "> 5 years"; }
}

public static class Format
{
    public static string Bytes(long n) { string[] u = ["B", "KB", "MB", "GB", "TB"]; double v = n; var i = 0; while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; } return $"{v:0.##} {u[i]}"; }
}
