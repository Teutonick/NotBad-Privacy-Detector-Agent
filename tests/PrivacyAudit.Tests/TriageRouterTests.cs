using PrivacyAudit.Core;

namespace PrivacyAudit.Tests;

public sealed class TriageRouterTests
{
    [Fact]
    public void RouterSendsTextConfigOnlyToApplicableScanners()
    {
        var path = Path.Combine(Path.GetTempPath(), $"triage-{Guid.NewGuid():N}.env");
        File.WriteAllText(path, "KEY=value");
        try
        {
            var finding = new Finding { Path = path, DisplayName = Path.GetFileName(path), Category = "Potential secrets", SizeBytes = 9, ExposureScore = 60 };

            var routes = new TriageRouter().Route(finding);

            Assert.Contains(routes, route => route.ScannerId == DetectionEvidenceCalculator.Secrets);
            Assert.Contains(routes, route => route.ScannerId == DetectionEvidenceCalculator.Configs);
            Assert.DoesNotContain(routes, route => route.ScannerId == DetectionEvidenceCalculator.People);
            Assert.DoesNotContain(routes, route => route.ScannerId == DetectionEvidenceCalculator.Documents);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RouterDownranksThumbnailWithoutExcludingIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"thumbnail-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, [0]);
        try
        {
            var finding = new Finding { Path = path, DisplayName = Path.GetFileName(path), Category = "Images", SizeBytes = 1, ExposureScore = 20 };
            var router = new TriageRouter();

            var small = router.Route(finding, new MediaImageDimensions(32, 32));
            var large = router.Route(finding, new MediaImageDimensions(3000, 2000));

            Assert.Contains(small, route => route.ScannerId == DetectionEvidenceCalculator.People);
            Assert.True(large.Single(route => route.ScannerId == DetectionEvidenceCalculator.People).Priority > small.Single(route => route.ScannerId == DetectionEvidenceCalculator.People).Priority);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RouterSendsVideoOnlyToBoundedImageSafetyAnalysis()
    {
        var path = Path.Combine(Path.GetTempPath(), $"triage-video-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(path, [0]);
        try
        {
            var finding = new Finding { Path = path, DisplayName = Path.GetFileName(path), Category = "Video", SizeBytes = 1, ExposureScore = 20 };
            var routes = new TriageRouter().Route(finding);

            Assert.Contains(routes, route => route.ScannerId == DetectionEvidenceCalculator.ImageSafety);
            Assert.DoesNotContain(routes, route => route.ScannerId == DetectionEvidenceCalculator.People);
            Assert.DoesNotContain(routes, route => route.ScannerId == DetectionEvidenceCalculator.Documents);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SelectionUsesFivePercentAndAbsoluteCap()
    {
        var root = Path.Combine(Path.GetTempPath(), $"triage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var findings = Enumerable.Range(0, 200).Select(index =>
            {
                var path = Path.Combine(root, $"config-{index}.json");
                File.WriteAllText(path, "{}");
                return new Finding { Path = path, DisplayName = Path.GetFileName(path), Category = "Other", SizeBytes = 2, ExposureScore = index % 100 };
            }).ToArray();

            var selection = new TriageRouter().Select(findings, absoluteLimit: 7);

            Assert.Equal(200, selection.EligibleFindings);
            Assert.Equal(10, selection.RequestedTenPercent);
            Assert.Equal(7, selection.SelectedFindings);
            Assert.All(selection.Routes, route => Assert.Contains(route.FindingId, selection.FindingIds));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SelectedImagesKeepEveryApplicableDeepScannerRoute()
    {
        var root = Path.Combine(Path.GetTempPath(), $"triage-images-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var findings = Enumerable.Range(0, 50).Select(index =>
            {
                var path = Path.Combine(root, $"photo-{index}.jpg");
                File.WriteAllBytes(path, [0]);
                return new Finding { Path = path, DisplayName = Path.GetFileName(path), Category = "Images", SizeBytes = 1, ExposureScore = index };
            }).ToArray();

            var selection = new TriageRouter().Select(findings);

            Assert.Equal(3, selection.SelectedFindings);
            foreach (var findingId in selection.FindingIds)
            {
                var scannerIds = selection.Routes.Where(route => route.FindingId == findingId).Select(route => route.ScannerId).ToHashSet();
                Assert.Contains(DetectionEvidenceCalculator.People, scannerIds);
                Assert.Contains(DetectionEvidenceCalculator.Documents, scannerIds);
                Assert.Contains(DetectionEvidenceCalculator.ImageSafety, scannerIds);
                Assert.Contains(DetectionEvidenceCalculator.Exif, scannerIds);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CandidateSelectionHonorsCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => new TriageRouter().Select(
            [new Finding { Path = "unused", DisplayName = "unused" }], token: source.Token));
    }

    [Fact]
    public async Task CoordinatorRunsRegisteredScannerAndPersistsCompletionState()
    {
        var finding = new Finding { Id = Guid.NewGuid(), Path = "virtual", DisplayName = "virtual" };
        var route = new TriageRouteDecision(finding.Id, "future-scanner", 80, DeepScannerCost.Cheap, "test", ["test route"]);
        var session = new PriorityAuditSession { AuditFingerprint = "audit", FindingIds = [finding.Id], Routes = [route] };
        var registry = new DeepAuditScannerRegistry([new FakeDeepScanner()]);

        await new PriorityAuditCoordinator(registry).RunAsync(session, new Dictionary<Guid, Finding> { [finding.Id] = finding }, (_, _) => { }, null, CancellationToken.None);

        Assert.Equal(PriorityAuditStatus.Completed, session.Status);
        Assert.Contains(PriorityAuditSession.RouteKey(finding.Id, "future-scanner"), session.CompletedRoutes);
        Assert.Equal(1, session.ConfirmedSignals);
    }

    [Fact]
    public void PriorityReportIsAvailableOnlyAfterSuccessfulCompletion()
    {
        var findingId = Guid.NewGuid();
        var session = new PriorityAuditSession
        {
            FindingIds = [findingId],
            Routes = [new(findingId, "pii", 70, DeepScannerCost.Moderate, "docs", ["text"])]
        };

        Assert.False(session.HasReport);
        session.CompletedRoutes.Add(PriorityAuditSession.RouteKey(findingId, "pii"));
        Assert.False(session.HasReport);
        session.Status = PriorityAuditStatus.Paused;
        Assert.False(session.HasReport);
        session.Status = PriorityAuditStatus.Canceled;
        Assert.False(session.HasReport);
        session.Status = PriorityAuditStatus.Completed;
        Assert.True(session.HasReport);
    }

    [Fact]
    public void PrioritySessionRoundTripsLocally()
    {
        var path = Path.Combine(Path.GetTempPath(), $"priority-session-{Guid.NewGuid():N}.json");
        var store = new PriorityAuditSessionStore(path);
        var findingId = Guid.NewGuid();
        var session = new PriorityAuditSession { AuditFingerprint = "audit", FindingIds = [findingId], Routes = [new(findingId, "pii", 70, DeepScannerCost.Moderate, "docs", ["text"])] };
        session.CompletedRoutes.Add(PriorityAuditSession.RouteKey(findingId, "pii"));
        try
        {
            store.Save(session);
            var restored = store.Load();

            Assert.NotNull(restored);
            Assert.Equal("audit", restored!.AuditFingerprint);
            Assert.Contains(PriorityAuditSession.RouteKey(findingId, "pii"), restored.CompletedRoutes);
            store.Delete();
            Assert.Null(store.Load());
        }
        finally { store.Delete(); }
    }

    [Fact]
    public void LegacyPrioritySessionLoadsWithOldSelectionPolicy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"priority-session-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"auditFingerprint\":\"audit\",\"status\":2}");

            var restored = new PriorityAuditSessionStore(path).Load();

            Assert.NotNull(restored);
            Assert.Equal(0, restored!.SelectionPolicyVersion);
            Assert.True(restored.SelectionPolicyVersion < PriorityAuditSession.CurrentSelectionPolicyVersion);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PrioritySessionStore_RecoversCompletedReportFromBackup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"priority-session-{Guid.NewGuid():N}.json");
        var store = new PriorityAuditSessionStore(path);
        try
        {
            var completed = new PriorityAuditSession
            {
                AuditFingerprint = "audit-v2",
                Status = PriorityAuditStatus.Completed,
                Routes = [new(Guid.NewGuid(), "pii", 70, DeepScannerCost.Moderate, "docs", ["text"])]
            };
            store.Save(completed);
            store.Save(new PriorityAuditSession { AuditFingerprint = "audit-v2", Status = PriorityAuditStatus.Ready });

            Assert.True(store.Load()!.HasReport);
            File.WriteAllText(path, "broken json");
            Assert.True(store.Load()!.HasReport);
        }
        finally { store.Delete(); }
    }

    sealed class FakeDeepScanner : IDeepAuditScanner
    {
        public string Id => "future-scanner";
        public string NameKey => "future-scanner";
        public Task<bool> IsAvailableAsync(CancellationToken token = default) => Task.FromResult(true);
        public Task<DeepScannerBatchResult> AnalyzeAsync(IReadOnlyList<Finding> findings, IProgress<DeepScannerProgress>? progress, CancellationToken token)
        {
            for (var index = 0; index < findings.Count; index++) progress?.Report(new(Id, index + 1, findings.Count, index + 1, 0, findings[index].Path));
            return Task.FromResult(new DeepScannerBatchResult(Id, findings.Count, findings.Count, 0));
        }
    }
}
