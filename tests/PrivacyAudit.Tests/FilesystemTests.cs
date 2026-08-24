using PrivacyAudit.Core;
using PrivacyAudit.Scanners;

namespace PrivacyAudit.Tests;

public sealed class FilesystemTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "PrivacyAuditTests", Guid.NewGuid().ToString("N"));
    public FilesystemTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Scanner_FindsImagesSecretsModelsAndHonorsExclusions()
    {
        File.WriteAllBytes(Path.Combine(_root, "photo.jpg"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(_root, ".env"), "never read by scanner");
        File.WriteAllBytes(Path.Combine(_root, "model.gguf"), [1]);
        var excluded = Directory.CreateDirectory(Path.Combine(_root, "excluded")).FullName;
        File.WriteAllText(Path.Combine(excluded, "secret.key"), "not scanned");
        var context = new ScanContext { Preset = ScanPreset.Full, Roots = [_root], Exclusions = [excluded], Progress = new Progress<ScanProgress>(), LargeFileThreshold = long.MaxValue };

        var result = await new FilesystemScanner().ScanAsync(context, CancellationToken.None);

        Assert.Contains(result.Findings, x => x.Category == "Images");
        Assert.Contains(result.Findings, x => x.Category == "Potential secrets");
        Assert.Contains(result.Findings, x => x.Category == "AI / Models");
        Assert.DoesNotContain(result.Findings, x => x.Path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Exclusions_AreCaseInsensitiveAndPrefixBased()
    {
        var context = new ScanContext { Preset = ScanPreset.Quick, Roots = [], Exclusions = [Path.Combine(_root, "Archive")], Progress = new Progress<ScanProgress>() };
        Assert.True(context.IsExcluded(Path.Combine(_root, "archive", "item.txt")));
        Assert.False(context.IsExcluded(Path.Combine(_root, "other", "item.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
