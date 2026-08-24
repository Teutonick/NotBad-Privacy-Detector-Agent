namespace PrivacyAudit.PeopleDetection;

public enum ModelDownloadStage
{
    Checking,
    Connecting,
    Downloading,
    Verifying,
    DownloadingLicense,
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
