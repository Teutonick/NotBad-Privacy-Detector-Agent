using PrivacyAudit.Core;

namespace PrivacyAudit.Scanners;

public sealed class FilesystemScanner : IPrivacyScanner
{
    public string Id => "filesystem";
    public string Name => "Filesystem";
    static readonly string[] SecretNames = [".env", "credentials.", "secrets.", "config.", "auth.", "token."];
    static readonly HashSet<string> SecretExt = new(StringComparer.OrdinalIgnoreCase) { ".pem", ".key", ".pfx", ".p12", ".kdbx" };
    static readonly HashSet<string> DevDirs = new(StringComparer.OrdinalIgnoreCase) { ".venv", "venv", "node_modules", ".gradle", "intermediates", "DerivedDataCache" };

    public async Task<ScannerResult> ScanAsync(ScanContext context, CancellationToken token) => await Task.Run(() => Scan(context, token), token);

    ScannerResult Scan(ScanContext context, CancellationToken token)
    {
        var start = DateTime.UtcNow; var findings = new List<Finding>(); int warnings = 0, errors = 0; long files = 0, bytes = 0;
        foreach (var root in context.Roots.Where(Directory.Exists))
        {
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0)
            {
                if (token.IsCancellationRequested) break;
                var dir = pending.Pop();
                if (context.IsExcluded(dir)) continue;
                try
                {
                    var di = new DirectoryInfo(dir);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    foreach (var child in Directory.EnumerateDirectories(dir))
                    {
                        if (context.IsExcluded(child)) continue;
                        var name = Path.GetFileName(child);
                        if (DevDirs.Contains(name)) findings.Add(MakeDirectory(child, "Development", "Environment", 10, "Development environment or regeneratable cache"));
                        pending.Push(child);
                    }
                    foreach (var path in Directory.EnumerateFiles(dir))
                    {
                        if (token.IsCancellationRequested) break;
                        try
                        {
                            var fi = new FileInfo(path); files++; bytes += fi.Length;
                            var category = Classifier.File(path); var name = fi.Name;
                            var isSecret = SecretExt.Contains(fi.Extension) || SecretNames.Any(x => name.Equals(x, StringComparison.OrdinalIgnoreCase) || name.StartsWith(x, StringComparison.OrdinalIgnoreCase));
                            var isGenericSourceConfig = isSecret && CredentialConfigDetector.IsGenericSourceConfig(path);
                            // A source module named config.* is only a candidate after a bounded,
                            // content-aware check. This avoids treating environment lookups and
                            // rate-limit flags as exposed credentials while retaining hard-coded secrets.
                            if (isGenericSourceConfig)
                            {
                                try
                                {
                                    // Large or unreadable candidates remain visible: failure to perform
                                    // the refinement must never suppress the original filename signal.
                                    if (fi.Length <= 1024 * 1024)
                                    {
                                        var text = TextExtractor.ExtractFromPlainText(path);
                                        isSecret = CredentialConfigDetector.Analyze(path, text).IsCredentialConfig ||
                                                   (!string.IsNullOrWhiteSpace(text) && SecretDetector.Scan(text, path).TotalMatches > 0);
                                    }
                                }
                                catch (Exception ex) { CrashLogger.LogException(ex, $"Filesystem config classification: {path}"); isSecret = true; }
                            }
                            // A generic config source stays in the audit even when its current
                            // content has no secret signal. Deep scanners may change metadata,
                            // but never remove an audited object from the result set.
                            var isInteresting = isSecret || isGenericSourceConfig || category != "Other" || fi.Length >= context.LargeFileThreshold;
                            if (isInteresting)
                            {
                                var (score, reasons) = Exposure(path);
                                var sub = isSecret ? "Potential secret" : fi.Length >= context.LargeFileThreshold ? "Large file" : "File";
                                if (isSecret) category = "Potential secrets";
                                findings.Add(new Finding { ScannerId = Id, Category = category, Subcategory = sub, Path = path, DisplayName = name, SizeBytes = fi.Length, CreatedAt = fi.CreationTime, ModifiedAt = fi.LastWriteTime, LastAccessAt = fi.LastAccessTime, ExposureScore = score, ExposureReasons = reasons });
                            }
                            if (files % 500 == 0) context.Progress.Report(new(Name, path, files, bytes, findings.Count));
                        }
                        catch (UnauthorizedAccessException) { warnings++; }
                        catch (IOException) { warnings++; }
                    }
                }
                catch (UnauthorizedAccessException) { warnings++; }
                catch (PathTooLongException) { warnings++; }
                catch (IOException) { errors++; }
            }
        }
        context.Progress.Report(new(Name, "", files, bytes, findings.Count, "Complete"));
        return new(Id, findings, warnings, errors, DateTime.UtcNow - start);
    }

    static Finding MakeDirectory(string p, string c, string s, int score, string reason) => new() { ScannerId = "filesystem", Category = c, Subcategory = s, Path = p, DisplayName = Path.GetFileName(p), IsDirectory = true, ExposureScore = score, ExposureReasons = [reason] };
    static (int, IReadOnlyList<string>) Exposure(string path)
    {
        var scores = new List<int>(); var reasons = new List<string>();
        void Add(string part, int score, string reason) { if (path.Contains(part, StringComparison.OrdinalIgnoreCase)) { scores.Add(score); reasons.Add(reason); } }
        Add("\\Desktop\\", 60, "Located on Desktop"); Add("\\Pictures\\", 60, "Located in Pictures"); Add("\\Downloads\\", 50, "Located in Downloads"); Add("\\Documents\\", 40, "Located in Documents"); Add("\\AppData\\Local\\Temp\\", 20, "Located in user Temp"); Add("\\AppData\\", 10, "Located in AppData");
        return (ExposureCalculator.Calculate(scores), reasons);
    }
}
