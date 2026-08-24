using System.Reflection;
using System.Text;

namespace PrivacyAudit.Core;

public sealed record DiagnosticIssueReport(string Title, string Body, string PathShape, string SizeRange)
{
    public IReadOnlyList<string> Labels { get; init; } = [];
}

public static class DiagnosticReportBuilder
{
    public static DiagnosticIssueReport Build(Finding finding, string correction, string? applicationVersion = null, string? windowsVersion = null, string? userExplanation = null)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var extension = finding.IsDirectory ? "[directory]" : NormalizeExtension(Path.GetExtension(finding.Path));
        var pathShape = BuildPathShape(finding.Path, finding.IsDirectory);
        var sizeRange = finding.IsDirectory ? "not applicable" : BucketSize(finding.SizeBytes);
        var version = applicationVersion ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";
        var os = windowsVersion ?? Environment.OSVersion.VersionString;
        var safeCorrection = NormalizeLabel(correction, "Unspecified");
        var scanner = NormalizeLabel(finding.ScannerId, "unknown");
        var category = NormalizeLabel(finding.Category, "Other");
        var subcategory = NormalizeLabel(finding.Subcategory, "none");
        var explanation = string.IsNullOrWhiteSpace(userExplanation) ? "[not provided]" : userExplanation.Trim()[..Math.Min(userExplanation.Trim().Length, 4000)];
        var correctionLabel = correction switch
        {
            "Wrong finding" => "wrong-finding",
            "Wrong file origin" => "wrong-file-origin",
            "Wrong risk level" => "wrong-risk-level",
            _ => "other-inaccuracy"
        };

        var body = new StringBuilder()
            .AppendLine("ОПИШИТЕ СЛОВАМИ, ЧТО КОНКРЕТНО НЕ ТАК С НАХОДКОЙ.")
            .AppendLine("Это описание нужно, чтобы команда поняла причину ошибки, а не только её категорию.")
            .AppendLine()
            .AppendLine("## Anonymized incorrect-detection report")
            .AppendLine()
            .AppendLine($"- Application version: `{version}`")
            .AppendLine($"- Finding type: `{category}`")
            .AppendLine($"- Detected: `{category} / {subcategory}`")
            .AppendLine($"- User correction: `{safeCorrection}`")
            .AppendLine($"- User description: {explanation}")
            .AppendLine($"- Path shape: `{pathShape}`")
            .AppendLine($"- Filename extension: `{extension}`")
            .AppendLine($"- Size range: `{sizeRange}`")
            .AppendLine($"- Scanner / rule ID: `{scanner}`")
            .AppendLine($"- Exposure score range: `{BucketScore(finding.ExposureScore)}`")
            .AppendLine($"- Provenance evidence: `{(finding.Subcategory.Length > 0 ? subcategory : "not available")}`")
            .AppendLine($"- Windows version: `{NormalizeOs(os)}`")
            .AppendLine()
            .AppendLine("### Privacy declaration")
            .AppendLine("This report was generated locally. It does not include file contents, full paths, exact file sizes or timestamps, detected secrets or PII values, GPS coordinates, Windows username, or hostname.")
            .ToString();

        return new DiagnosticIssueReport($"Incorrect detection: {category}", body, pathShape, sizeRange)
        {
            Labels = ["privacy-audit", "incorrect-detection", correctionLabel]
        };
    }

    public static string BuildPathShape(string? path, bool isDirectory = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return "unknown";
        var normalized = path.Replace('/', '\\');
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var isUnc = normalized.StartsWith("\\\\", StringComparison.Ordinal);
        var start = parts.Length > 0 && parts[0].EndsWith(':') ? 1 : isUnc ? Math.Min(2, parts.Length) : 0;
        var directoryCount = Math.Max(0, parts.Length - start - (isDirectory ? 0 : 1));
        var tokens = new List<string> { isUnc ? "NETWORK_ROOT" : "USERPROFILE" };
        tokens.AddRange(Enumerable.Repeat("directory", Math.Min(directoryCount, 6)));
        if (directoryCount > 6) tokens.Add("…");
        if (!isDirectory)
        {
            var extension = NormalizeExtension(Path.GetExtension(normalized));
            tokens.Add(extension == "[none]" ? "file" : $"file{extension}");
        }
        return string.Join(" / ", tokens);
    }

    public static string BucketSize(long bytes) => bytes switch
    {
        < 0 => "unknown",
        < 10 * 1024 => "0–10 KB",
        < 100 * 1024 => "10–100 KB",
        < 1024 * 1024 => "100 KB–1 MB",
        < 10L * 1024 * 1024 => "1–10 MB",
        < 100L * 1024 * 1024 => "10–100 MB",
        < 1024L * 1024 * 1024 => "100 MB–1 GB",
        _ => "1 GB or larger"
    };

    static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return "[none]";
        var safe = new string(extension.ToLowerInvariant().Where(c => c == '.' || char.IsAsciiLetterOrDigit(c)).Take(16).ToArray());
        return safe.StartsWith('.') && safe.Length > 1 ? safe : "[other]";
    }

    static string NormalizeLabel(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var safe = new string(value.Where(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-' or '/' or '.').Take(80).ToArray()).Trim();
        return safe.Length == 0 ? fallback : safe;
    }

    static string BucketScore(int score) => score switch { < 20 => "0–19", < 40 => "20–39", < 60 => "40–59", < 80 => "60–79", _ => "80–100" };
    static string NormalizeOs(string value)
    {
        var numeric = new string(value.Where(c => char.IsAsciiDigit(c) || c == '.').ToArray())
            .Split('.', StringSplitOptions.RemoveEmptyEntries);
        return numeric.Length >= 2 ? $"Windows {numeric[0]}.{numeric[1]}" : "Windows (version unavailable)";
    }
}
