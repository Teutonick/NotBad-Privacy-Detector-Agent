using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrivacyAudit.Core;

public sealed class ArchiveEntryInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size_bytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("is_sensitive")] public bool IsSensitive { get; set; }
    [JsonPropertyName("sensitivity_category")] public string SensitivityCategory { get; set; } = "";
}

public sealed class ArchiveInspectionResult
{
    [JsonPropertyName("is_archive")] public bool IsArchive { get; set; }
    [JsonPropertyName("total_entries")] public int TotalEntries { get; set; }
    [JsonPropertyName("sensitive_entries_count")] public int SensitiveEntriesCount { get; set; }
    [JsonPropertyName("privacy_score")] public string PrivacyScore { get; set; } = "Low";
    [JsonPropertyName("entries")] public List<ArchiveEntryInfo> SensitiveEntries { get; set; } = [];
    [JsonPropertyName("tree_view")] public string TreeView { get; set; } = "";

    public static string Serialize(ArchiveInspectionResult result) => JsonSerializer.Serialize(result);

    public static bool TryParse(string? json, out ArchiveInspectionResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("archive_inspection", out var prop)) return false;
            result = JsonSerializer.Deserialize<ArchiveInspectionResult>(prop.GetRawText());
            return result is not null;
        }
        catch
        {
            return false;
        }
    }

    public static string InjectIntoMetadata(string currentJson, ArchiveInspectionResult result)
    {
        try
        {
            var dict = string.IsNullOrWhiteSpace(currentJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(currentJson) ?? new();
            dict["archive_inspection"] = result;
            return JsonSerializer.Serialize(dict);
        }
        catch
        {
            return JsonSerializer.Serialize(new { archive_inspection = result });
        }
    }
}

public static class ArchiveInspector
{
    static readonly HashSet<string> SupportedArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".jar", ".war", ".ear", ".nupkg", ".apk", ".vsix"
    };

    public static bool IsSupportedArchive(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return SupportedArchiveExtensions.Contains(ext);
    }

    public static ArchiveInspectionResult Inspect(string filePath)
    {
        var result = new ArchiveInspectionResult();
        if (!File.Exists(filePath) || !IsSupportedArchive(filePath)) return result;

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            result.IsArchive = true;
            result.TotalEntries = archive.Entries.Count;

            var sensitiveList = new List<ArchiveEntryInfo>();
            bool hasCritical = false;
            bool hasHigh = false;
            bool hasMedium = false;

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name)) continue; // skip directories

                var (isSensitive, category, level) = EvaluateEntry(entry.FullName);
                if (isSensitive)
                {
                    sensitiveList.Add(new ArchiveEntryInfo
                    {
                        Name = entry.FullName,
                        SizeBytes = entry.Length,
                        IsSensitive = true,
                        SensitivityCategory = category
                    });

                    if (level == "Critical") hasCritical = true;
                    else if (level == "High") hasHigh = true;
                    else if (level == "Medium") hasMedium = true;
                }
            }

            result.SensitiveEntries = sensitiveList.Take(100).ToList();
            result.SensitiveEntriesCount = sensitiveList.Count;

            result.PrivacyScore = hasCritical ? "Critical" : hasHigh ? "High" : hasMedium ? "Medium" : sensitiveList.Count > 0 ? "Low" : "Safe";

            // Build Tree View
            var sb = new StringBuilder();
            sb.AppendLine($"{Path.GetFileName(filePath)} ({result.TotalEntries} files, {result.SensitiveEntriesCount} sensitive):");

            for (int i = 0; i < result.SensitiveEntries.Count; i++)
            {
                var entry = result.SensitiveEntries[i];
                var isLast = i == result.SensitiveEntries.Count - 1;
                var branch = isLast ? "    └─ " : "    ├─ ";
                sb.AppendLine($"{branch}{entry.Name} [{entry.SensitivityCategory}] ({FormatBytes(entry.SizeBytes)})");
            }

            if (result.SensitiveEntriesCount > result.SensitiveEntries.Count)
            {
                sb.AppendLine($"    ... and {result.SensitiveEntriesCount - result.SensitiveEntries.Count} more sensitive files inside.");
            }

            result.TreeView = sb.ToString().TrimEnd();
        }
        catch
        {
            // Graceful fallback on encrypted or corrupt archives
        }

        return result;
    }

    static (bool IsSensitive, string Category, string Level) EvaluateEntry(string entryName)
    {
        var lower = entryName.ToLowerInvariant();
        var fileName = Path.GetFileName(lower);

        // 1. Passwords, private keys, crypto wallets
        if (fileName.Contains("password") || fileName.Contains("passwords") ||
            fileName.EndsWith(".pem") || fileName.EndsWith(".key") || fileName.Equals("id_rsa") || fileName.Equals("id_ed25519") ||
            fileName.Equals("wallet.dat") || fileName.Contains("seed_phrase") || fileName.Contains("mnemonic"))
        {
            return (true, "Private Key / Password File", "Critical");
        }

        // 2. Identity documents & scans
        if (fileName.Contains("passport") || fileName.Contains("паспорт") ||
            fileName.Contains("snils") || fileName.Contains("снилс") ||
            fileName.Contains("inn") || fileName.Contains("инн") ||
            fileName.Contains("id_card") || fileName.Contains("driver_license") || fileName.Contains("права") ||
            fileName.Contains("zagran") || fileName.Contains("загран"))
        {
            return (true, "ID / Passport Document Scan", "Critical");
        }

        // 3. Environment & config secrets
        if (fileName.StartsWith(".env") || fileName.Equals(".npmrc") || fileName.Equals("kubeconfig") ||
            fileName.Equals("credentials") || fileName.Equals(".git-credentials") || fileName.Equals(".pgpass") || fileName.Equals(".my.cnf"))
        {
            return (true, "Environment & Auth Secret", "High");
        }

        // 4. Database dumps & backup exports
        if (fileName.EndsWith(".sql") || fileName.Contains("backup_db") || fileName.Contains("db_dump") ||
            fileName.Contains("users_export") || fileName.Contains("clients_export") || fileName.Contains("database.sqlite"))
        {
            return (true, "Database / User Data Export", "High");
        }

        // 5. Personal tax & financial documents
        if (fileName.Contains("ndfl") || fileName.Contains("ндфл") || fileName.Contains("salary") || fileName.Contains("зарплата") ||
            fileName.Contains("statement") || fileName.Contains("выписка") || fileName.Contains("dogovor") || fileName.Contains("договор"))
        {
            return (true, "Financial / Personal Document", "Medium");
        }

        return (false, "", "Low");
    }

    static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
