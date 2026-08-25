namespace PrivacyAudit.Core;

public sealed record PageSlice<T>(IReadOnlyList<T> Items, int PageIndex, int PageCount, int TotalCount, int PageSize);

public static class FindingPagination
{
    // Deliberately larger than the viewport: a page must be long enough to exercise
    // scroll-boundary loading instead of switching while a short page is visible.
    public const int ListPageSize = 600;
    public const int LoadedPageWindow = 6;

    public static int TilePageSize(double tileSize)
    {
        if (double.IsNaN(tileSize) || double.IsInfinity(tileSize)) tileSize = 140;
        tileSize = Math.Clamp(tileSize, 80, 260);
        return Math.Clamp((int)Math.Round(240 * Math.Pow(140 / tileSize, 2)), 72, 600);
    }

    public static PageSlice<T> Slice<T>(IReadOnlyList<T> globallySortedItems, int requestedPage, int pageSize)
    {
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var pageCount = Math.Max(1, (int)Math.Ceiling(globallySortedItems.Count / (double)pageSize));
        var page = ((requestedPage % pageCount) + pageCount) % pageCount;
        var items = globallySortedItems.Skip(page * pageSize).Take(pageSize).ToArray();
        return new(items, page, pageCount, globallySortedItems.Count, pageSize);
    }

    public static double RestoreViewportOffset(double originalOffset, double originalItemTop, double currentItemTop, double scrollableHeight) =>
        Math.Clamp(originalOffset + currentItemTop - originalItemTop, 0, Math.Max(0, scrollableHeight));

    public static IEnumerable<Finding> Sort(IEnumerable<Finding> source, string property, bool descending)
    {
        return property switch
        {
            nameof(Finding.RiskLevel) => Apply(source, x => x.RiskLevel, descending),
            nameof(Finding.ExposureScore) => Apply(source, PrivacyRadarRanking.Score, descending),
            nameof(Finding.PersonalAttentionScore) => Apply(source, x => x.PersonalAttentionScore ?? -1, descending),
            nameof(Finding.ObjectivePrivacyRisk) => Apply(source, x => x.ObjectivePrivacyRisk, descending),
            nameof(Finding.CombinedPriority) => Apply(source, x => x.CombinedPriority, descending),
            nameof(Finding.PrivacyRiskRank) => Apply(source, x => x.PrivacyRiskRank, descending),
            nameof(Finding.DetectionPriorityRank) => Apply(source, x => x.DetectionPriorityRank, descending),
            nameof(Finding.Category) => Apply(source, x => x.Category, descending),
            nameof(Finding.DisplayName) => Apply(source, x => x.DisplayName, descending),
            nameof(Finding.Path) => Apply(source, x => x.Path, descending),
            nameof(Finding.SizeBytes) => Apply(source, x => x.SizeBytes, descending),
            nameof(Finding.ModifiedAt) => Apply(source, x => x.ModifiedAt ?? DateTime.MinValue, descending),
            _ => Apply(source, x => x.ModifiedAt ?? DateTime.MinValue, descending)
        };
    }

    static IEnumerable<Finding> Apply<TKey>(IEnumerable<Finding> source, Func<Finding, TKey> key, bool descending) =>
        descending ? source.OrderByDescending(key) : source.OrderBy(key);
}
