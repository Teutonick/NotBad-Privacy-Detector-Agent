namespace PrivacyAudit.Core;

public sealed class ScanCoordinator(IEnumerable<IPrivacyScanner> scanners)
{
    readonly IReadOnlyList<IPrivacyScanner> _scanners = scanners.ToList();
    public async Task<(IReadOnlyList<Finding> Findings, IReadOnlyList<ScannerResult> Runs)> RunAsync(ScanContext context, CancellationToken token)
    {
        var findings = new List<Finding>(); var runs = new List<ScannerResult>();
        foreach (var scanner in _scanners)
        {
            if (token.IsCancellationRequested) break;
            context.Progress.Report(new(scanner.Name, "", 0, 0, findings.Count, "Starting"));
            try { var result = await scanner.ScanAsync(context, token); runs.Add(result); findings.AddRange(result.Findings); }
            catch (OperationCanceledException) { break; }
            catch { runs.Add(new(scanner.Id, [], 0, 1, TimeSpan.Zero)); }
        }
        return (findings, runs);
    }
}
