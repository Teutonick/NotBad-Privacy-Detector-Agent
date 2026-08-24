using PrivacyAudit.Core;
using System.Collections.Specialized;

namespace PrivacyAudit.Tests;

public sealed class PaginationTests
{
    [Fact]
    public void Pagination_WrapsCircularlyAndKeepsOnlyCurrentPage()
    {
        var items = Enumerable.Range(0, 1250).ToArray();
        var last = FindingPagination.Slice(items, -1, 600);
        Assert.Equal(2, last.PageIndex);
        Assert.Equal(3, last.PageCount);
        Assert.Equal(50, last.Items.Count);
        Assert.Equal(1200, last.Items[0]);
        Assert.Equal(0, FindingPagination.Slice(items, 3, 600).PageIndex);
    }

    [Fact]
    public void GlobalSort_HappensBeforePagination()
    {
        var findings = Enumerable.Range(0, 1000).Select(i => new Finding { DisplayName = i.ToString(), Path = i.ToString(), SizeBytes = i }).ToArray();
        var sorted = FindingPagination.Sort(findings, nameof(Finding.SizeBytes), true).ToArray();
        var secondPage = FindingPagination.Slice(sorted, 1, 600);
        Assert.Equal(399, secondPage.Items[0].SizeBytes);
        Assert.Equal(0, secondPage.Items[^1].SizeBytes);
    }

    [Theory]
    [InlineData(80, 600)]
    [InlineData(140, 240)]
    [InlineData(260, 72)]
    public void TilePageSize_AdaptsToThumbnailScale(double tileSize, int expected) => Assert.Equal(expected, FindingPagination.TilePageSize(tileSize));

    [Fact]
    public void RangeCollection_ReplacesLargeWindowsWithSingleUiNotification()
    {
        var collection = new ObservableRangeCollection<int>();
        var notifications = 0;
        NotifyCollectionChangedAction? action = null;
        collection.CollectionChanged += (_, e) => { notifications++; action = e.Action; };

        collection.ReplaceRange(Enumerable.Range(0, 600));

        Assert.Equal(600, collection.Count);
        Assert.Equal(1, notifications);
        Assert.Equal(NotifyCollectionChangedAction.Reset, action);
    }

    [Fact]
    public void LoadedPageWindow_IsTwiceThePreviousThreePageBuffer()
    {
        Assert.Equal(6, FindingPagination.LoadedPageWindow);
    }
}
