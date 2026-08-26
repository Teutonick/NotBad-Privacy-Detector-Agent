using System.Runtime.InteropServices;

namespace PrivacyAudit.Core;

public sealed record StartupPrerequisiteIssue(string Code, string TitleKey, string MessageKey);

/// <summary>
/// Checks the only optional Windows feature used by the self-contained build.
/// Managed runtime, SQLite and ONNX Runtime binaries ship inside the application.
/// </summary>
public static class StartupPrerequisiteChecker
{
    public static IReadOnlyList<StartupPrerequisiteIssue> Check() => CheckLibraries(TryLoadLibrary);

    public static IReadOnlyList<StartupPrerequisiteIssue> CheckLibraries(Func<string, bool> libraryAvailable)
    {
        ArgumentNullException.ThrowIfNull(libraryAvailable);
        if (libraryAvailable("mfplat.dll") && libraryAvailable("mfreadwrite.dll")) return [];
        return [new("media_foundation_missing", "StartupComponentsTitle", "StartupMediaFoundationMissing")];
    }

    static bool TryLoadLibrary(string name)
    {
        if (!NativeLibrary.TryLoad(name, out var handle)) return false;
        NativeLibrary.Free(handle);
        return true;
    }
}
