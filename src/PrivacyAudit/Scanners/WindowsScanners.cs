using System.Security.Principal;
using PrivacyAudit.Core;

namespace PrivacyAudit.Scanners;

public sealed class RecentScanner : IPrivacyScanner
{
    public string Id => "recent"; public string Name => "Windows Recent";
    public async Task<ScannerResult> ScanAsync(ScanContext context, CancellationToken token) => await Task.Run(() =>
    {
        var start = DateTime.UtcNow; var list = new List<Finding>(); int warnings = 0;
        var recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        try
        {
            foreach (var p in Directory.EnumerateFiles(recent))
            {
                if (token.IsCancellationRequested) break;
                var f = new FileInfo(p);
                var isShortcut = f.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
                var target = isShortcut ? RecentShortcutResolver.TryGetTarget(p) : null;
                var targetExists = !string.IsNullOrWhiteSpace(target) && (File.Exists(target) || Directory.Exists(target));
                var reasons = target is null
                    ? new[] { "Present in Windows Recent" }
                    : targetExists
                        ? new[] { "Present in Windows Recent", $"Shortcut target is available: {target}" }
                        : new[] { "Present in Windows Recent", $"Shortcut target is no longer available: {target}" };
                list.Add(new()
                {
                    ScannerId = Id,
                    Category = "Recent",
                    Subcategory = !isShortcut ? "Reference" : targetExists ? "Shortcut reference" : "Stale shortcut reference",
                    Path = p,
                    DisplayName = f.Name,
                    SizeBytes = f.Length,
                    ModifiedAt = f.LastWriteTime,
                    ExposureScore = 100,
                    ExposureReasons = reasons
                });
            }
        }
        catch { warnings++; }
        return new ScannerResult(Id, list, warnings, 0, DateTime.UtcNow - start);
    }, token);
}

public sealed class JumpListScanner : IPrivacyScanner
{
    public string Id => "jump-lists"; public string Name => "Application file history";
    public ApplicationHistorySummary Summary { get; private set; } = new(0, 0, null, 0);
    public async Task<ScannerResult> ScanAsync(ScanContext context, CancellationToken token) => await Task.Run(() =>
    {
        var start = DateTime.UtcNow;
        token.ThrowIfCancellationRequested();
        Summary = ApplicationHistoryDiscovery.Summarize(ApplicationHistoryDiscovery.EnumerateContainers());
        return new ScannerResult(Id, [], Summary.Warnings, 0, DateTime.UtcNow - start);
    }, token);
}

public sealed class ProfileScanner : IPrivacyScanner
{
    public string Id => "profiles"; public string Name => "Windows Profiles";
    public Task<ScannerResult> ScanAsync(ScanContext context, CancellationToken token)
    {
        var start = DateTime.UtcNow; var list = new List<Finding>(); int warnings = 0;
        try { foreach (var p in Directory.EnumerateDirectories(Path.GetPathRoot(Environment.SystemDirectory)! + "Users")) { var d = new DirectoryInfo(p); list.Add(new() { ScannerId = Id, Category = "User profile", Subcategory = "Profile (account status unknown)", Path = p, DisplayName = d.Name, IsDirectory = true, ModifiedAt = d.LastWriteTime, ExposureScore = 0, ExposureReasons = ["Windows user profile exists"] }); } } catch { warnings++; }
        return Task.FromResult(new ScannerResult(Id, list, warnings, 0, DateTime.UtcNow - start));
    }
}

public static class Elevation { public static bool IsAdministrator() { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } }
