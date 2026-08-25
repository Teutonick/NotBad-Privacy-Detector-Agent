namespace PrivacyAudit.Core;

public enum MediaScanOperationStatus
{
    Ready,
    Running,
    Paused,
    Completed,
    Canceled
}

/// <summary>Independent UI lifecycle for one Media analyzer.</summary>
public sealed class MediaScanOperationState
{
    public MediaScanOperationStatus Status { get; private set; } = MediaScanOperationStatus.Ready;
    public bool IsRunning => Status == MediaScanOperationStatus.Running;
    public bool IsPaused => Status == MediaScanOperationStatus.Paused;
    public bool CanStart => !IsRunning;
    public bool CanPause => IsRunning;
    public bool CanCancel => IsRunning || IsPaused;

    public void Start() => Status = MediaScanOperationStatus.Running;
    public void Pause() => Status = MediaScanOperationStatus.Paused;
    public void Complete() => Status = MediaScanOperationStatus.Completed;
    public void Cancel() => Status = MediaScanOperationStatus.Canceled;
    public void Reset() => Status = MediaScanOperationStatus.Ready;
}
