namespace PrivacyAudit.Core;

public sealed record PriorityAuditProgress(
    double OverallProgress,
    string ScannerId,
    int ScannerCompleted,
    int ScannerTotal,
    int CompletedRoutes,
    int TotalRoutes,
    int ConfirmedSignals,
    int Errors,
    string CurrentPath);

public sealed class PriorityAuditCoordinator(DeepAuditScannerRegistry registry)
{
    public async Task RunAsync(
        PriorityAuditSession session,
        IReadOnlyDictionary<Guid, Finding> findings,
        Action<PriorityAuditSession, PriorityAuditProgress> onProgress,
        Func<string, IReadOnlyList<Finding>, Task>? onStageCompleted,
        CancellationToken token)
    {
        var started = DateTime.UtcNow;
        session.Status = PriorityAuditStatus.Running;
        var scannerOrder = session.Routes.Select(route => route.ScannerId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var scannerId in scannerOrder)
        {
            token.ThrowIfCancellationRequested();
            var routes = session.Routes.Where(route => route.ScannerId.Equals(scannerId, StringComparison.OrdinalIgnoreCase))
                .Where(route => !session.CompletedRoutes.Contains(PriorityAuditSession.RouteKey(route.FindingId, route.ScannerId)))
                .Where(route => !session.SkippedRoutes.Contains(PriorityAuditSession.RouteKey(route.FindingId, route.ScannerId)))
                .ToArray();
            if (routes.Length == 0) continue;

            if (!registry.TryGet(scannerId, out var scanner) || !await scanner.IsAvailableAsync(token))
            {
                foreach (var route in routes) session.SkippedRoutes.Add(PriorityAuditSession.RouteKey(route.FindingId, route.ScannerId));
                session.SkippedScanners++;
                Report(session, onProgress, scannerId, 0, routes.Length, "");
                continue;
            }

            var batch = routes.Select(route => findings.GetValueOrDefault(route.FindingId)).Where(finding => finding is not null).Cast<Finding>().ToArray();
            if (batch.Length == 0)
            {
                foreach (var route in routes) session.SkippedRoutes.Add(PriorityAuditSession.RouteKey(route.FindingId, route.ScannerId));
                Report(session, onProgress, scannerId, 0, routes.Length, "");
                continue;
            }

            session.CurrentScannerId = scannerId;
            session.CurrentScannerCompleted = 0;
            session.CurrentScannerTotal = batch.Length;
            var pathMap = batch.GroupBy(finding => finding.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
            var previousErrors = 0;
            var progress = new Progress<DeepScannerProgress>(value =>
            {
                session.CurrentScannerCompleted = value.Completed;
                session.CurrentScannerTotal = value.Total;
                if (pathMap.TryGetValue(value.CurrentPath, out var findingId))
                {
                    session.CompletedRoutes.Add(PriorityAuditSession.RouteKey(findingId, scannerId));
                    if (value.Errors > previousErrors) session.FailedRoutes.Add(PriorityAuditSession.RouteKey(findingId, scannerId));
                }
                previousErrors = value.Errors;
                session.Elapsed += DateTime.UtcNow - started;
                started = DateTime.UtcNow;
                Report(session, onProgress, scannerId, value.Completed, value.Total, value.CurrentPath);
            });

            try
            {
                var result = await scanner.AnalyzeAsync(batch, progress, token);
                foreach (var route in routes) session.CompletedRoutes.Add(PriorityAuditSession.RouteKey(route.FindingId, route.ScannerId));
                session.ConfirmedSignals += result.Confirmed;
                session.Errors += result.Errors;
                foreach (var failedId in result.FailedFindingIds ?? []) session.FailedRoutes.Add(PriorityAuditSession.RouteKey(failedId, scannerId));
                if (onStageCompleted is not null) await onStageCompleted(scannerId, batch);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, $"Priority audit scanner stage: {scannerId}");
                foreach (var route in routes) session.SkippedRoutes.Add(PriorityAuditSession.RouteKey(route.FindingId, route.ScannerId));
                session.Errors++;
            }
            session.Elapsed += DateTime.UtcNow - started;
            started = DateTime.UtcNow;
            Report(session, onProgress, scannerId, batch.Length, batch.Length, "");
        }

        session.CurrentScannerId = "";
        session.Status = PriorityAuditStatus.Completed;
        Report(session, onProgress, "", 0, 0, "");
    }

    static void Report(PriorityAuditSession session, Action<PriorityAuditSession, PriorityAuditProgress> callback, string scannerId, int completed, int total, string path) =>
        callback(session, new(session.Progress, scannerId, completed, total, session.CompletedRouteCount, session.TotalRoutes, session.ConfirmedSignals, session.Errors, path));
}
