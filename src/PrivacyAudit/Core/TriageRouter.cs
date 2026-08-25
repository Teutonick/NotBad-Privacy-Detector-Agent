namespace PrivacyAudit.Core;

public enum DeepScannerCost { Cheap = 1, Moderate = 3, Expensive = 8 }

public sealed record TriageRouteDecision(
    Guid FindingId,
    string ScannerId,
    int Priority,
    DeepScannerCost Cost,
    string DiversityKey,
    IReadOnlyList<string> Reasons);

public sealed record TriageSelection(
    IReadOnlyList<Guid> FindingIds,
    IReadOnlyList<TriageRouteDecision> Routes,
    int EligibleFindings,
    int RequestedTenPercent,
    int AbsoluteLimit,
    int CostBudget)
{
    public int SelectedFindings => FindingIds.Count;
}

public sealed class TriageRouter
{
    public const int DefaultAbsoluteLimit = 25_000;
    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".avif" };
    static readonly HashSet<string> ExifExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".tif", ".tiff", ".heic", ".png", ".webp", ".docx", ".xlsx", ".pptx" };
    static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".env", ".ini", ".cfg", ".conf", ".config", ".json", ".xml", ".yaml", ".yml", ".toml", ".properties", ".rc", ".reg", ".ovpn" };
    static readonly string[] SensitiveTokens =
        { "passport", "паспорт", "secret", "token", "password", "credential", "private", "personal", "contract", "договор", "инн", "снилс", "identity", "backup", "prod", "production" };
    static readonly string[] DemoTokens =
        { "demo", "sample", "example", "default", "template", "fixture", "mock", "vendor", "node_modules", "packages", "cache", "thumbnail", "thumb" };

    public IReadOnlyList<TriageRouteDecision> Route(Finding finding, MediaImageDimensions? dimensions = null)
    {
        if (finding.Ignored || finding.IsDirectory || string.IsNullOrWhiteSpace(finding.Path) || !File.Exists(finding.Path)) return [];
        var ext = Path.GetExtension(finding.Path);
        var text = $"{finding.DisplayName} {finding.Path}";
        var baseScore = 25 + Math.Min(25, finding.ExposureScore / 4);
        var reasons = new List<string>();
        if (finding.ExposureScore >= 60) { baseScore += 10; reasons.Add("high initial exposure"); }
        if (SensitiveTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase))) { baseScore += 15; reasons.Add("sensitive name or path"); }
        var demoPenalty = DemoTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase)) ? 18 : 0;
        if (demoPenalty > 0) reasons.Add("demo/default/cache path penalty");

        var routes = new List<TriageRouteDecision>();
        void Add(string scanner, int suitability, DeepScannerCost cost, params string[] routeReasons)
        {
            if (DetectionEvidenceCalculator.IsCompleted(finding.MetadataJson, scanner)) return;
            var priority = Math.Clamp(baseScore + suitability - demoPenalty, 1, 100);
            var directory = Path.GetDirectoryName(finding.Path) ?? "";
            var diversityKey = $"{finding.Category}|{Path.GetExtension(finding.Path)}|{directory}";
            routes.Add(new(finding.Id, scanner, priority, cost, diversityKey, reasons.Concat(routeReasons).Distinct().ToArray()));
        }

        if (TextExtractor.IsSupported(finding.Path))
        {
            var compactTextBonus = finding.SizeBytes is > 0 and <= 5 * 1024 * 1024 ? 8 : -8;
            Add(DetectionEvidenceCalculator.Pii, 18 + compactTextBonus, DeepScannerCost.Moderate, "text-extractable format");
            Add(DetectionEvidenceCalculator.Secrets, 20 + compactTextBonus + (ConfigExtensions.Contains(ext) ? 10 : 0), DeepScannerCost.Moderate, "text-extractable format");
            Add(DetectionEvidenceCalculator.Identity, 14 + compactTextBonus, DeepScannerCost.Moderate, "text-extractable format");
        }

        if (TextExtractor.IsSupported(finding.Path) || ConfigExtensions.Contains(ext) || finding.DisplayName.StartsWith(".env", StringComparison.OrdinalIgnoreCase))
            Add(DetectionEvidenceCalculator.Configs, ConfigExtensions.Contains(ext) ? 32 : 12, DeepScannerCost.Moderate, "configuration-like format or name");

        if (ArchiveInspector.IsSupportedArchive(finding.Path))
            Add(DetectionEvidenceCalculator.Archives, finding.SizeBytes <= 512L * 1024 * 1024 ? 24 : 4, DeepScannerCost.Expensive, "supported archive format");

        if (ImageExtensions.Contains(ext) || finding.Category.Equals("Images", StringComparison.OrdinalIgnoreCase))
        {
            var image = dimensions;
            var minSide = image is { } d ? Math.Min(d.Width, d.Height) : 0;
            var pixels = image?.PixelCount ?? 0;
            var fullImageBonus = minSide >= 480 && pixels >= 500_000 ? 24 : minSide > 0 ? -12 : 0;
            var documentNameBonus = SensitiveTokens.Any(token => finding.DisplayName.Contains(token, StringComparison.OrdinalIgnoreCase)) ? 12 : 0;
            Add(DetectionEvidenceCalculator.Documents, 16 + fullImageBonus + documentNameBonus, DeepScannerCost.Expensive, "image candidate for document detection");
            Add(DetectionEvidenceCalculator.People, 14 + fullImageBonus, DeepScannerCost.Expensive, "image candidate for face detection");
            if (ExifExtensions.Contains(ext)) Add(DetectionEvidenceCalculator.Exif, 22, DeepScannerCost.Cheap, "format may contain metadata");
        }
        else if (ExifExtensions.Contains(ext))
        {
            Add(DetectionEvidenceCalculator.Exif, 14, DeepScannerCost.Cheap, "office format may contain metadata");
        }

        return routes;
    }

    public TriageSelection Select(IReadOnlyList<Finding> findings, int absoluteLimit = DefaultAbsoluteLimit)
    {
        var routes = new List<TriageRouteDecision>();
        foreach (var finding in findings)
        {
            MediaImageDimensions? dimensions = null;
            if (finding.Category.Equals("Images", StringComparison.OrdinalIgnoreCase) && MediaImageInfo.TryReadDimensions(finding.Path, out var read)) dimensions = read;
            routes.AddRange(Route(finding, dimensions));
        }

        var eligible = routes.Select(route => route.FindingId).Distinct().Count();
        var requested = eligible == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(eligible * 0.10));
        var uniqueLimit = Math.Min(requested, Math.Max(1, absoluteLimit));
        var costBudget = Math.Max(uniqueLimit * 24, 1);
        if (uniqueLimit == 0) return new([], [], 0, 0, absoluteLimit, 0);

        var queues = routes.GroupBy(route => route.ScannerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new Queue<TriageRouteDecision>(Diversify(group)), StringComparer.OrdinalIgnoreCase);
        var selectedIds = new HashSet<Guid>();
        var selectedRoutes = new List<TriageRouteDecision>();
        var selectedPairs = new HashSet<(Guid, string)>();
        var spent = 0;

        while (queues.Count > 0 && selectedIds.Count < uniqueLimit)
        {
            var progressed = false;
            foreach (var scanner in queues.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray())
            {
                var queue = queues[scanner];
                while (queue.Count > 0)
                {
                    var route = queue.Dequeue();
                    if (selectedIds.Contains(route.FindingId)) continue;
                    if (!selectedPairs.Add((route.FindingId, route.ScannerId))) continue;
                    var cost = (int)route.Cost;
                    if (spent + cost > costBudget) continue;
                    selectedIds.Add(route.FindingId);
                    selectedRoutes.Add(route);
                    spent += cost;
                    progressed = true;
                    break;
                }
                if (queue.Count == 0) queues.Remove(scanner);
                if (selectedIds.Count >= uniqueLimit) break;
            }
            if (!progressed) break;
        }

        // Once the diverse unique scope is fixed, attach every applicable route that still fits the operation budget.
        foreach (var route in routes.Where(route => selectedIds.Contains(route.FindingId)).OrderByDescending(route => route.Priority))
        {
            if (!selectedPairs.Add((route.FindingId, route.ScannerId))) continue;
            var cost = (int)route.Cost;
            if (spent + cost > costBudget) continue;
            selectedRoutes.Add(route);
            spent += cost;
        }

        return new(selectedIds.ToArray(), selectedRoutes, eligible, requested, absoluteLimit, costBudget);
    }

    static IEnumerable<TriageRouteDecision> Diversify(IEnumerable<TriageRouteDecision> routes)
    {
        var queues = routes.OrderByDescending(route => route.Priority)
            .GroupBy(route => route.DiversityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new Queue<TriageRouteDecision>(group))
            .ToList();
        while (queues.Count > 0)
        {
            foreach (var queue in queues.ToArray())
            {
                if (queue.Count > 0) yield return queue.Dequeue();
                if (queue.Count == 0) queues.Remove(queue);
            }
        }
    }
}
