using System.Buffers.Binary;
using System.Text;
using PrivacyAudit.Core;
using PrivacyAudit.Scanners;

namespace PrivacyAudit.Tests;

public sealed class ApplicationHistoryTests
{
    [Fact]
    public void KnownAppIdIsResolvedEntirelyOffline()
    {
        var identity = ApplicationIdentityResolver.Resolve(@"C:\History\9839aec31243a928.automaticDestinations-ms");

        Assert.Equal("Microsoft Excel", identity.DisplayName);
        Assert.Equal(ApplicationIdentityConfidence.Known, identity.Confidence);
    }

    [Fact]
    public void UnknownAppIdStaysExplicitlyUnknown()
    {
        var identity = ApplicationIdentityResolver.Resolve(@"C:\History\0123456789abcdef.automaticDestinations-ms");

        Assert.Equal("0123456789abcdef", identity.AppId);
        Assert.Equal("Unknown application", identity.DisplayName);
        Assert.Equal(ApplicationIdentityConfidence.Unknown, identity.Confidence);
    }

    [Fact]
    public void ShellLinkReadsUnicodeRelativeTargetWithoutOpeningIt()
    {
        var target = @"C:\Users\Nikita\Documents\passport.xlsx";
        var data = BuildRelativePathLink(target);

        Assert.Equal(target, ShellLinkReader.TryReadTarget(data));
    }

    [Fact]
    public void ShellLinkReadsCyrillicAnsiTarget()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var target = @"D:\Документы\Договор.docx";
        var encoded = Encoding.GetEncoding(1251).GetBytes(target);
        var data = new byte[76 + 2 + encoded.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 0x4c);
        new byte[] { 0x01,0x14,0x02,0x00,0x00,0x00,0x00,0x00,0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46 }.CopyTo(data, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), 0x08);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(76, 2), (ushort)encoded.Length);
        encoded.CopyTo(data, 78);

        Assert.Equal(target, ShellLinkReader.TryReadTarget(data, 1251));
    }

    [Fact]
    public void ShellLinkRepairsUtf8BytesMisreadAsAnsi()
    {
        var target = @"C:\Users\Никита\Документы\Договор.docx";
        var encoded = Encoding.UTF8.GetBytes(target);
        var data = new byte[76 + 2 + encoded.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 0x4c);
        new byte[] { 0x01,0x14,0x02,0x00,0x00,0x00,0x00,0x00,0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46 }.CopyTo(data, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), 0x08);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(76, 2), (ushort)encoded.Length);
        encoded.CopyTo(data, 78);

        Assert.Equal(target, ShellLinkReader.TryReadTarget(data, 1251));
    }

    [Fact]
    public async Task JumpListScannerDoesNotCreateContainerFindings()
    {
        var scanner = new JumpListScanner();
        var context = new ScanContext
        {
            Preset = ScanPreset.Custom,
            Roots = [Path.GetTempPath()],
            Exclusions = [],
            Progress = new Progress<ScanProgress>()
        };

        var result = await scanner.ScanAsync(context, CancellationToken.None);

        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData(@"C:\Projects\client.env", 75)]
    [InlineData(@"\\NAS\Documents\readme.txt", 30)]
    [InlineData(@"D:\Games\readme.txt", 0)]
    public void HistoricalRiskOnlyPromotesMeaningfulPaths(string path, int expected)
    {
        Assert.Equal(expected, HistoricalPathRisk.Score(path));
    }

    [Theory]
    [InlineData(true, true, 4)]
    [InlineData(true, false, 3)]
    [InlineData(false, true, 2)]
    [InlineData(false, false, 1)]
    public void ApplicationHistoryOrderingPrioritizesAvailabilityAndMenuState(bool existsNow, bool isPinned, int expectedPriority)
    {
        var entry = new ApplicationHistoryEntry("C:\\history\\item.pdf", null, 0, isPinned, existsNow, "test", "test");

        Assert.Equal(expectedPriority, ApplicationHistoryOrdering.Priority(entry));
    }

    [Fact]
    public void ApplicationHistoryOrderingPlacesAvailablePinnedEntriesFirst()
    {
        var entries = new[]
        {
            new ApplicationHistoryEntry("missing-pinned", null, 0, true, false, "test", "test"),
            new ApplicationHistoryEntry("available", null, 0, false, true, "test", "test"),
            new ApplicationHistoryEntry("available-pinned", null, 0, true, true, "test", "test")
        };

        Assert.Equal(["available-pinned", "available", "missing-pinned"], ApplicationHistoryOrdering.OrderEntries(entries).Select(x => x.TargetPath).ToArray());
    }


    static byte[] BuildRelativePathLink(string target)
    {
        var encoded = Encoding.Unicode.GetBytes(target);
        var data = new byte[76 + 2 + encoded.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 0x4c);
        new byte[] { 0x01,0x14,0x02,0x00,0x00,0x00,0x00,0x00,0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46 }.CopyTo(data, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, 4), 0x88); // HasRelativePath | IsUnicode
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(76, 2), (ushort)target.Length);
        encoded.CopyTo(data, 78);
        return data;
    }
}
