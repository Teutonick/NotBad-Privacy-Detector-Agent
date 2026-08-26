using PrivacyAudit.Core;

namespace PrivacyAudit.Tests;

public sealed class MediaScanOperationStateTests
{
    [Fact]
    public void PeopleAndDocumentStatesCanProgressIndependently()
    {
        var people = new MediaScanOperationState();
        var documents = new MediaScanOperationState();

        people.Start();
        people.Pause();
        documents.Start();

        Assert.True(people.IsPaused);
        Assert.True(people.CanStart);
        Assert.True(people.CanCancel);
        Assert.True(documents.IsRunning);
        Assert.True(documents.CanPause);

        people.Cancel();
        documents.Complete();

        Assert.Equal(MediaScanOperationStatus.Canceled, people.Status);
        Assert.Equal(MediaScanOperationStatus.Completed, documents.Status);
        Assert.False(people.IsRunning);
        Assert.False(documents.IsRunning);
    }

    [Fact]
    public void ImageSafetyScanCanPauseResumeAndCancelWithoutLosingItsLifecycle()
    {
        var imageSafety = new MediaScanOperationState();

        imageSafety.Start();
        imageSafety.Pause();

        Assert.True(imageSafety.IsPaused);
        Assert.True(imageSafety.CanStart);
        Assert.True(imageSafety.CanCancel);

        imageSafety.Start();
        Assert.True(imageSafety.IsRunning);

        imageSafety.Cancel();
        Assert.Equal(MediaScanOperationStatus.Canceled, imageSafety.Status);
    }
}
