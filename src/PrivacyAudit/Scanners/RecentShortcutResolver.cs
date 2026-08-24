namespace PrivacyAudit.Scanners;

/// <summary>
/// Reads the target recorded in a Windows Recent .lnk without opening the target.
/// A broken target is useful audit evidence, so callers decide how to classify it.
/// </summary>
internal static class RecentShortcutResolver
{
    public static string? TryGetTarget(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            var target = (string?)shortcut.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }
}
