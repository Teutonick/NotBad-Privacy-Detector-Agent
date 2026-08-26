namespace PrivacyAudit.PeopleDetection;

/// <summary>
/// Explicit install lifecycle states. Scanners are permitted to load a model only
/// when the status is <see cref="InstalledVerified"/>.
/// </summary>
public enum ModelStatus
{
    /// <summary>Model files are not present on disk.</summary>
    NotInstalled,
    /// <summary>Download is currently in progress.</summary>
    Downloading,
    /// <summary>SHA-256 integrity check is in progress.</summary>
    Verifying,
    /// <summary>Model is present and its SHA-256 matches the pinned manifest digest.</summary>
    InstalledVerified,
    /// <summary>Model file exists but its SHA-256 does not match the manifest. Must not be used for inference.</summary>
    Corrupted
}

public enum ModelDownloadStage
{
    Checking,
    Connecting,
    /// <summary>Content-Length received from server; verifying it does not exceed <c>MaximumAllowedSize</c>.</summary>
    SizeCheck,
    Downloading,
    Verifying,
    Installing,
    Completed
}

public sealed record ModelDownloadProgress(
    ModelDownloadStage Stage,
    long BytesReceived = 0,
    long? TotalBytes = null)
{
    public double? Fraction => TotalBytes is > 0 ? Math.Min(1, BytesReceived / (double)TotalBytes.Value) : null;
}

public sealed class ModelDownloadException(string message, string code, Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
