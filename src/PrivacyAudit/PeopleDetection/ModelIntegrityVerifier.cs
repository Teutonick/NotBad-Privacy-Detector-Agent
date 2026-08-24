using System.Security.Cryptography;

namespace PrivacyAudit.PeopleDetection;

public sealed class ModelIntegrityVerifier
{
    public async Task<bool> VerifyAsync(string path, ModelManifest manifest, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public bool Verify(string path, ModelManifest manifest)
    {
        if (!File.Exists(path)) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
