using PrivacyAudit.Core;

namespace PrivacyAudit.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public async Task ScannerFailure_DoesNotStopFollowingScanners()
    {
        var coordinator = new ScanCoordinator([new ThrowingScanner(), new FindingScanner()]);
        var result = await coordinator.RunAsync(Context(), CancellationToken.None);
        Assert.Single(result.Findings);
        Assert.Equal(2, result.Runs.Count);
        Assert.Equal(1, result.Runs[0].Errors);
    }

    [Fact]
    public async Task Cancellation_ReturnsAlreadyCollectedFindings()
    {
        using var cts = new CancellationTokenSource();
        var coordinator = new ScanCoordinator([new FindingScanner(() => cts.Cancel()), new FindingScanner()]);
        var result = await coordinator.RunAsync(Context(), cts.Token);
        Assert.Single(result.Findings);
        Assert.Single(result.Runs);
    }

    static ScanContext Context() => new() { Preset = ScanPreset.Quick, Roots = [], Exclusions = [], Progress = new Progress<ScanProgress>() };

    sealed class ThrowingScanner : IPrivacyScanner
    {
        public string Id => "bad"; public string Name => "Bad";
        public Task<ScannerResult> ScanAsync(ScanContext context, CancellationToken cancellationToken) => throw new IOException("expected");
    }
    sealed class FindingScanner(Action? completed = null) : IPrivacyScanner
    {
        public string Id => "good"; public string Name => "Good";
        public Task<ScannerResult> ScanAsync(ScanContext context, CancellationToken cancellationToken)
        {
            completed?.Invoke();
            Finding[] findings = [new() { ScannerId = Id, Path = "x", DisplayName = "x" }];
            return Task.FromResult(new ScannerResult(Id, findings, 0, 0, TimeSpan.Zero));
        }
    }
}
