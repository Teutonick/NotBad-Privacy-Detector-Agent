using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace PrivacyAudit.Core;

public static class FileProvenanceSchema
{
    public const int Version = 1;
}

public sealed record ProvenanceEvidence(string EvidenceType, string Description, int Weight, string Source);
public sealed record FileProvenanceResult(
    Guid FindingId, string Path, long FileSize, DateTime? FileModifiedAt, int EngineVersion,
    string? ApplicationName, string? Publisher, string? ExecutablePath, string ApplicationStatus,
    string OwnerType, string FileRole, int ConfidenceScore, string ConfidenceLevel,
    bool PossibleOrphan, bool PossibleCache, bool PossibleUserData, string DetectedFormat,
    IReadOnlyList<string> SchemaHints, IReadOnlyList<string> Neighbors, IReadOnlyList<ProvenanceEvidence> Evidence,
    DateTime AnalyzedAt)
{
    public bool IsCurrent(Finding finding) => Path.Equals(finding.Path, StringComparison.OrdinalIgnoreCase) &&
        FileSize == finding.SizeBytes && FileModifiedAt == finding.ModifiedAt && EngineVersion == FileProvenanceSchema.Version;
}

public sealed class FileProvenanceAnalyzer
{
    static readonly object InventoryGate = new();
    static DateTime _inventoryBuiltAt;
    static IReadOnlyList<InstalledApp> _installedInventory = [];
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> DirectoryFingerprints = new(StringComparer.OrdinalIgnoreCase);
    public async Task<FileProvenanceResult> AnalyzeAsync(Finding finding, CancellationToken token, IProgress<string>? progress = null)
        => await Task.Run(() => Analyze(finding, token, progress), token);

    FileProvenanceResult Analyze(Finding finding, CancellationToken token, IProgress<string>? progress)
    {
        token.ThrowIfCancellationRequested();
        var evidence = new List<ProvenanceEvidence>(); var hints = new List<string>(); var neighbors = new List<string>();
        progress?.Report("Path");
        var fullPath = Path.GetFullPath(finding.Path); var parts = fullPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var pathLower = fullPath.ToLowerInvariant();
        var role = InferRole(pathLower, finding.DisplayName, out var cache, out var userData);
        var ownerType = pathLower.Contains("\\packages\\") ? "APPLICATION_DATA" : pathLower.Contains("\\program files") ? "APPLICATION_RESOURCE" : userData ? "USER_CONTENT" : "UNKNOWN";
        AddPathEvidence(pathLower, evidence);
        progress?.Report("Application");
        string? app = null; string? publisher = null; string? exe = null; var installed = "Unknown";
        var appName = parts.FirstOrDefault(x => x.Equals("Packages", StringComparison.OrdinalIgnoreCase)) is not null ? parts.ElementAtOrDefault(Array.IndexOf(parts, "Packages") + 1) : null;
        if (!string.IsNullOrWhiteSpace(appName)) { app = appName; evidence.Add(new("APPX_PATH", $"Path is inside AppX package {appName}", 35, "path")); }
        foreach (var ancestor in Ancestors(fullPath, 8))
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(ancestor)) continue;
            foreach (var candidate in Directory.EnumerateFiles(ancestor, "*.exe", SearchOption.TopDirectoryOnly).Take(8))
            {
                try { var info = FileVersionInfo.GetVersionInfo(candidate); if (!string.IsNullOrWhiteSpace(info.ProductName) || !string.IsNullOrWhiteSpace(info.CompanyName)) { app ??= info.ProductName; publisher ??= info.CompanyName; exe ??= candidate; evidence.Add(new("EXECUTABLE", $"Related executable: {Path.GetFileName(candidate)}", 20, "parent directory")); break; } } catch { }
            }
            if (app is not null) break;
        }
        if (app is not null) { progress?.Report("Registry"); var match = FindInstalledApplication(app, token); installed = match is null ? "Not found" : "Installed"; publisher ??= match?.Publisher; exe ??= match?.Executable; evidence.Add(new("APPLICATION_STATUS", match is null ? $"No installed application matches {app}" : $"Installed application matches {app}", match is null ? 12 : 20, "uninstall registry")); }
        progress?.Report("Directory");
        foreach (var ancestor in Ancestors(fullPath, 8).Take(3))
        {
            token.ThrowIfCancellationRequested();
            var fingerprint = DirectoryFingerprints.GetOrAdd(ancestor, static directory =>
            {
                try { return Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly).Take(40).Select(Path.GetFileName).Where(x => x is not null).Cast<string>().ToArray(); }
                catch { return []; }
            });
            neighbors.AddRange(fingerprint);
            if (neighbors.Any(x => x.Equals("package.json", StringComparison.OrdinalIgnoreCase) || x.Equals("manifest", StringComparison.OrdinalIgnoreCase))) evidence.Add(new("DIRECTORY_FINGERPRINT", "Parent directory contains application manifest/package metadata", 15, ancestor));
        }
        progress?.Report("Forensics");
        var format = DetectFormat(fullPath, token, out var formatEvidence); if (formatEvidence is not null) evidence.Add(formatEvidence);
        var schema = ReadSqliteSchema(fullPath, token); hints.AddRange(schema); if (schema.Count > 0) evidence.Add(new("SQLITE_SCHEMA", $"SQLite schema contains: {string.Join(", ", schema.Take(8))}", 25, "read-only schema"));
        var ads = ReadZoneIdentifier(fullPath); if (ads is not null) evidence.Add(new("MARK_OF_THE_WEB", ads, 10, "NTFS ADS"));
        if (PiiDetectionResult.TryParse(finding.MetadataJson, out var pii) && pii?.TotalMatches > 0) { hints.Add("PII"); evidence.Add(new("EXISTING_SCANNER", $"Existing PII scanner found {pii.TotalMatches} match(es)", 15, "finding metadata")); }
        if (SecretDetectionResult.TryParse(finding.MetadataJson, out var sec) && sec?.TotalMatches > 0) { hints.Add("Secrets"); evidence.Add(new("EXISTING_SCANNER", $"Existing secret scanner found {sec.TotalMatches} match(es)", 15, "finding metadata")); }
        if (CredentialConfigResult.TryParse(finding.MetadataJson, out var cfg) && cfg?.IsCredentialConfig == true) { hints.Add("Credentials/configuration"); evidence.Add(new("EXISTING_SCANNER", "Existing credential/config scanner classified this file", 15, "finding metadata")); }
        if (app is null && schema.Any(x => x.Equals("users", StringComparison.OrdinalIgnoreCase) || x.Equals("accounts", StringComparison.OrdinalIgnoreCase) || x.Equals("sessions", StringComparison.OrdinalIgnoreCase))) role = "ACCOUNT_PROFILE";
        var score = Math.Clamp(evidence.Sum(x => x.Weight), 0, 100); var level = score >= 80 ? "HIGH" : score >= 55 ? "MEDIUM" : score >= 25 ? "LOW" : "UNKNOWN";
        var orphan = app is not null && installed == "Not found"; if (orphan) evidence.Add(new("POSSIBLE_ORPHAN", "Likely application data has no matching installed application", 20, "registry correlation"));
        return new(finding.Id, finding.Path, finding.SizeBytes, finding.ModifiedAt, FileProvenanceSchema.Version, app, publisher, exe, installed, ownerType, role, score, level, orphan, cache, userData, format, hints.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), neighbors.Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray(), evidence, DateTime.UtcNow);
    }

    static IEnumerable<string> Ancestors(string path, int max) { var d = Path.GetDirectoryName(path); for (var i = 0; d is not null && i < max; i++, d = Directory.GetParent(d)?.FullName) yield return d; }
    static void AddPathEvidence(string p, List<ProvenanceEvidence> e) { if (p.Contains("\\appdata\\roaming\\")) e.Add(new("PATH", "Path is under AppData\\Roaming", 15, "path")); else if (p.Contains("\\appdata\\local\\")) e.Add(new("PATH", "Path is under AppData\\Local", 12, "path")); if (p.Contains("\\downloads\\")) e.Add(new("PATH", "Path is under Downloads", 8, "path")); }
    static string InferRole(string p, string name, out bool cache, out bool userData) { cache = p.Contains("cache") || p.Contains("temp") || p.Contains("thumbnail"); userData = p.Contains("documents") || p.Contains("desktop") || p.Contains("pictures") || p.Contains("downloads"); if (p.Contains("session") || name.Contains("session", StringComparison.OrdinalIgnoreCase)) return "SESSION"; if (p.Contains("history")) return "HISTORY"; if (p.Contains("config") || Path.GetExtension(name).Equals(".ini", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(name).Equals(".json", StringComparison.OrdinalIgnoreCase)) return "CONFIGURATION"; if (cache) return "CACHE"; if (p.Contains("profile") || p.EndsWith(".db")) return "APPLICATION_STATE"; return userData ? "USER_CONTENT" : "UNKNOWN"; }
    static string DetectFormat(string path, CancellationToken token, out ProvenanceEvidence? evidence) { evidence = null; try { using var s = File.OpenRead(path); var b = new byte[16]; var n = s.Read(b, 0, b.Length); token.ThrowIfCancellationRequested(); var header = Encoding.ASCII.GetString(b, 0, n); var f = header.StartsWith("SQLite format 3", StringComparison.Ordinal) ? "SQLite" : n >= 4 && b[0] == 0x50 && b[1] == 0x4B ? "ZIP" : n >= 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 ? "PDF" : n >= 2 && b[0] == 0x4D && b[1] == 0x5A ? "PE" : n >= 3 && b[0] == 0xFF && b[1] == 0xD8 ? "JPEG" : Path.GetExtension(path).TrimStart('.').ToUpperInvariant(); evidence = new("FORMAT", $"Detected format: {f}", 8, "magic bytes"); return f; } catch { var fallback = path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ? "SQLite" : "Unknown"; return fallback; } }
    static List<string> ReadSqliteSchema(string path, CancellationToken token) { var result = new List<string>(); if (!path.EndsWith(".db", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)) return result; try { using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Mode=ReadOnly;Cache=Shared"); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','index') LIMIT 40"; using var r = cmd.ExecuteReader(); while (r.Read()) { token.ThrowIfCancellationRequested(); result.Add(r.GetString(0)); } } catch { } return result; }
    static string? ReadZoneIdentifier(string path) { try { var ads = path + ":Zone.Identifier"; if (!File.Exists(ads)) return null; var text = File.ReadAllText(ads); var host = text.Split('\n').FirstOrDefault(x => x.StartsWith("HostUrl=", StringComparison.OrdinalIgnoreCase)); return host is null ? "Zone.Identifier is present" : $"Zone.Identifier: {host.Trim()}"; } catch { return null; } }
    sealed record InstalledApp(string Name, string? Publisher, string? Executable);
    static InstalledApp? FindInstalledApplication(string name, CancellationToken token)
    {
        IReadOnlyList<InstalledApp> inventory;
        lock (InventoryGate)
        {
            if (_installedInventory.Count == 0 || DateTime.UtcNow - _inventoryBuiltAt > TimeSpan.FromMinutes(10))
            {
                var values = new List<InstalledApp>();
                foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
                foreach (var sub in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                {
                    try
                    {
                        using var key = root.OpenSubKey(sub);
                        foreach (var childName in key?.GetSubKeyNames() ?? [])
                        {
                            token.ThrowIfCancellationRequested(); using var child = key!.OpenSubKey(childName); var display = child?.GetValue("DisplayName") as string;
                            if (!string.IsNullOrWhiteSpace(display)) values.Add(new(display, child!.GetValue("Publisher") as string, child.GetValue("DisplayIcon") as string));
                        }
                    }
                    catch { }
                }
                _installedInventory = values; _inventoryBuiltAt = DateTime.UtcNow;
            }
            inventory = _installedInventory;
        }
        return inventory.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }
}
