using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace PrivacyAudit.Core;

public enum ApplicationIdentityConfidence { Unknown, Probable, Known }

public sealed record ApplicationHistorySummary(int Containers, long TotalBytes, DateTime? LastModified, int Warnings);

public sealed record ApplicationIdentity(string AppId, string DisplayName, ApplicationIdentityConfidence Confidence);

public sealed record ApplicationHistoryEntry(
    string TargetPath,
    DateTime? LastInteraction,
    int InteractionCount,
    bool IsPinned,
    bool ExistsNow,
    string SourceContainer,
    string SourceKind,
    Guid? RelatedFindingId = null,
    RiskLevel RelatedRisk = RiskLevel.None,
    int HistoricalExposureScore = 0,
    bool IsDirectory = false,
    long SizeBytes = 0,
    DateTime? TargetModifiedAt = null,
    string ApplicationKey = "",
    string ApplicationName = "Unknown application") : INotifyPropertyChanged
{
    bool? _personalAttentionLabel;
    float? _personalAttentionScore;
    public string LastInteractionDisplay => LastInteraction?.ToString("g") ?? "—";
    public string InteractionCountDisplay => InteractionCount > 0 ? InteractionCount.ToString("N0") : "—";
    public RiskLevel EffectiveRisk => RelatedRisk != RiskLevel.None ? RelatedRisk : ExposureCalculator.Level(HistoricalExposureScore);
    public string SizeDisplay => IsDirectory ? "—" : Format.Bytes(SizeBytes);
    public bool? PersonalAttentionLabel { get => _personalAttentionLabel; set { if (_personalAttentionLabel == value) return; _personalAttentionLabel = value; OnChanged(); } }
    public float? PersonalAttentionScore { get => _personalAttentionScore; set { if (_personalAttentionScore == value) return; _personalAttentionScore = value; OnChanged(); OnChanged(nameof(PersonalAttentionDisplay)); } }
    public string PersonalAttentionDisplay => PersonalAttentionScore is float score ? $"{score:0}%" : "—";
    public event PropertyChangedEventHandler? PropertyChanged;
    void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed record ApplicationHistoryApplication(
    ApplicationIdentity Identity,
    IReadOnlyList<ApplicationHistoryEntry> Entries,
    int ContainerCount,
    int Warnings)
{
    public int MissingTargets => Entries.Count(x => !x.ExistsNow);
    public int SensitiveObjects => Entries.Count(x => x.EffectiveRisk >= RiskLevel.High);
    public DateTime? LastInteraction => Entries.Count == 0 ? null : Entries.Max(x => x.LastInteraction);
    public float? PersonalAttentionScore
    {
        get
        {
            var scores = Entries.Where(x => x.PersonalAttentionScore is not null).Select(x => x.PersonalAttentionScore!.Value)
                .OrderByDescending(x => x).Take(3).ToArray();
            return scores.Length == 0 ? null : scores.Average();
        }
    }
    public string PersonalAttentionDisplay => PersonalAttentionScore is float score ? $"{score:0}%" : "—";
}

public sealed record ApplicationHistoryAnalysis(
    IReadOnlyList<ApplicationHistoryApplication> Applications,
    int Containers,
    int Warnings,
    TimeSpan Duration,
    IReadOnlyList<Finding> SignificantFindings)
{
    public int RememberedObjects => Applications.Sum(x => x.Entries.Count);
    public int MissingTargets => Applications.Sum(x => x.Entries.Count(e => !e.ExistsNow));
    public int SensitiveObjects => Applications.Sum(x => x.Entries.Count(e => e.EffectiveRisk >= RiskLevel.High));
}

public static class ApplicationHistoryDiscovery
{
    public static IReadOnlyList<string> EnumerateContainers()
    {
        var recent = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(recent)) return [];
        var root = Path.Combine(recent, "Microsoft", "Windows", "Recent");
        var result = new List<string>();
        foreach (var folder in new[] { "AutomaticDestinations", "CustomDestinations" })
        {
            try
            {
                var path = Path.Combine(root, folder);
                if (!Directory.Exists(path)) continue;
                result.AddRange(Directory.EnumerateFiles(path, "*Destinations-ms", SearchOption.TopDirectoryOnly));
            }
            catch { }
        }
        return result;
    }

    public static ApplicationHistorySummary Summarize(IEnumerable<string> containers)
    {
        long bytes = 0; DateTime? last = null; int count = 0, warnings = 0;
        foreach (var path in containers)
        {
            try
            {
                var info = new FileInfo(path);
                bytes += info.Length; count++;
                if (last is null || info.LastWriteTime > last) last = info.LastWriteTime;
            }
            catch { warnings++; }
        }
        return new(count, bytes, last, warnings);
    }
}

public static class ApplicationIdentityResolver
{
    // Offline-only table. Unknown hashes remain explicitly unknown rather than being looked up online.
    static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["b8ab77100df80ab2"] = "Microsoft Word",
        ["9839aec31243a928"] = "Microsoft Excel",
        ["d00655d2aa12ff6d"] = "Microsoft PowerPoint",
        ["f01b4d95cf55d32a"] = "Windows Explorer",
        ["5f7b5f1e01b83767"] = "Windows Explorer",
        ["1b4dd67f29cb1962"] = "Windows Explorer",
        ["9b9cdc69c1c24e2b"] = "Notepad",
        ["918e0ecb43d17e23"] = "Notepad",
        ["5d696d521de238c3"] = "Google Chrome",
        ["9d1f905ce5044aee"] = "Mozilla Firefox",
        ["d7528034b5bd6f28"] = "Windows Photos",
        ["a7bd71699cd38d1c"] = "Visual Studio",
        ["e70d383b15687e37"] = "Visual Studio Code"
    };

    public static ApplicationIdentity Resolve(string containerPath)
    {
        var appId = Path.GetFileName(containerPath).Split('.', 2)[0];
        return Known.TryGetValue(appId, out var name)
            ? new(appId, name, ApplicationIdentityConfidence.Known)
            : new(appId, "Unknown application", ApplicationIdentityConfidence.Unknown);
    }
}

public sealed class ApplicationHistoryAnalyzer
{
    const long MaxContainerBytes = 128L * 1024 * 1024;

    public async Task<ApplicationHistoryAnalysis> AnalyzeAsync(
        IEnumerable<string> containerPaths,
        IReadOnlyList<Finding> findings,
        IProgress<(int Done, int Total, string Current)>? progress,
        CancellationToken token)
    {
        var started = DateTime.UtcNow;
        var containers = containerPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var findingIndex = findings.Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .GroupBy(x => NormalizePath(x.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(f => f.ExposureScore).First(), StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, (ApplicationIdentity Identity, List<ApplicationHistoryEntry> Entries, int Containers, int Warnings)>(StringComparer.OrdinalIgnoreCase);
        int warnings = 0;

        for (var i = 0; i < containers.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var path = containers[i];
            progress?.Report((i, containers.Length, path));
            var identity = ApplicationIdentityResolver.Resolve(path);
            if (!groups.TryGetValue(identity.AppId, out var group)) group = (identity, [], 0, 0);
            group.Containers++;
            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaxContainerBytes) throw new InvalidDataException("Container size is outside the safe parsing limit.");
                var parsed = await Task.Run(() => ParseContainer(path, token), token);
                foreach (var raw in parsed)
                {
                    var normalized = NormalizePath(raw.TargetPath);
                    findingIndex.TryGetValue(normalized, out var related);
                    var isDirectory = Directory.Exists(raw.TargetPath);
                    var exists = isDirectory || File.Exists(raw.TargetPath);
                    long size = 0; DateTime? modified = null;
                    try
                    {
                        if (isDirectory) modified = new DirectoryInfo(raw.TargetPath).LastWriteTime;
                        else if (exists) { var targetInfo = new FileInfo(raw.TargetPath); size = targetInfo.Length; modified = targetInfo.LastWriteTime; }
                    }
                    catch { }
                    group.Entries.Add(raw with
                    {
                        ExistsNow = exists,
                        IsDirectory = isDirectory,
                        SizeBytes = size,
                        TargetModifiedAt = modified,
                        RelatedFindingId = related?.Id,
                        RelatedRisk = related?.RiskLevel ?? RiskLevel.None,
                        HistoricalExposureScore = related is null ? HistoricalPathRisk.Score(raw.TargetPath) : 0
                    });
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { warnings++; group.Warnings++; }
            groups[identity.AppId] = group;
        }

        progress?.Report((containers.Length, containers.Length, ""));
        var applications = groups.Values
            .Select(g => new ApplicationHistoryApplication(g.Identity,
                g.Entries.GroupBy(e => NormalizePath(e.TargetPath), StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.OrderByDescending(e => e.LastInteraction).First() with { InteractionCount = x.Max(e => e.InteractionCount), IsPinned = x.Any(e => e.IsPinned) })
                    .ToArray(), g.Containers, g.Warnings))
            .Where(x => x.Entries.Count > 0)
            .GroupBy(x => x.Identity.Confidence == ApplicationIdentityConfidence.Unknown ? $"unknown:{x.Identity.AppId}" : $"known:{x.Identity.DisplayName}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var identity = first.Identity.Confidence == ApplicationIdentityConfidence.Unknown
                    ? first.Identity with { DisplayName = $"Unknown application · {first.Identity.AppId}" }
                    : first.Identity;
                var applicationKey = identity.Confidence == ApplicationIdentityConfidence.Unknown ? identity.AppId : identity.DisplayName;
                var applicationName = identity.Confidence == ApplicationIdentityConfidence.Unknown ? "Unknown application" : identity.DisplayName;
                var entries = group.SelectMany(x => x.Entries).GroupBy(e => NormalizePath(e.TargetPath), StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.OrderByDescending(e => e.LastInteraction).First() with
                    {
                        InteractionCount = x.Max(e => e.InteractionCount), IsPinned = x.Any(e => e.IsPinned),
                        ApplicationKey = applicationKey, ApplicationName = applicationName
                    })
                    .OrderByDescending(e => e.LastInteraction).ToArray();
                return new ApplicationHistoryApplication(identity, entries, group.Sum(x => x.ContainerCount), group.Sum(x => x.Warnings));
            })
            .OrderByDescending(x => x.Entries.Count).ThenBy(x => x.Identity.DisplayName).ToArray();
        var significant = applications.SelectMany(app => app.Entries.Select(entry => (app.Identity.DisplayName, Entry: entry)))
            .Where(x => x.Entry.RelatedFindingId is null && x.Entry.HistoricalExposureScore >= 30)
            .GroupBy(x => NormalizePath(x.Entry.TargetPath), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var best = group.OrderByDescending(x => x.Entry.HistoricalExposureScore).First();
                var apps = string.Join(", ", group.Select(x => x.DisplayName).Distinct(StringComparer.CurrentCultureIgnoreCase));
                return new Finding
                {
                    ScannerId = "application-history", Category = "Application history",
                    Subcategory = best.Entry.ExistsNow ? "Remembered path outside the current audit" : "Historical path to a missing object",
                    Path = best.Entry.TargetPath,
                    DisplayName = Path.GetFileName(best.Entry.TargetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    ModifiedAt = best.Entry.LastInteraction, ExposureScore = best.Entry.HistoricalExposureScore,
                    ExposureReasons = best.Entry.ExistsNow
                        ? [$"Windows application history retains this path through {apps}"]
                        : [$"The object no longer exists, but Windows application history retains its path through {apps}"],
                    ApplicationHistoryReferences = apps, ApplicationHistoryLastSeen = best.Entry.LastInteraction,
                    ApplicationHistoryInteractionCount = best.Entry.InteractionCount
                };
            }).ToArray();
        return new(applications, containers.Length, warnings, DateTime.UtcNow - started, significant);
    }

    static IReadOnlyList<ApplicationHistoryEntry> ParseContainer(string path, CancellationToken token)
    {
        var kind = path.Contains("CustomDestinations", StringComparison.OrdinalIgnoreCase) ? "Custom" : "Automatic";
        if (kind == "Custom") return ParseCustomDestinations(path, token);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var compound = CompoundFile.Read(stream);
        var destList = compound.Streams.FirstOrDefault(x => x.Name.Equals("DestList", StringComparison.OrdinalIgnoreCase));
        var metadata = destList?.Data is { Length: > 32 } bytes ? DestListReader.Read(bytes) : new Dictionary<long, DestListMetadata>();
        var entries = new List<ApplicationHistoryEntry>();
        foreach (var item in compound.Streams)
        {
            token.ThrowIfCancellationRequested();
            if (item.Name.Equals("DestList", StringComparison.OrdinalIgnoreCase)) continue;
            if (!long.TryParse(item.Name, System.Globalization.NumberStyles.HexNumber, null, out var id)) id = -1;
            var target = ShellLinkReader.TryReadTarget(item.Data);
            if (string.IsNullOrWhiteSpace(target)) continue;
            metadata.TryGetValue(id, out var meta);
            entries.Add(new(target, meta?.LastInteraction, meta?.InteractionCount ?? 0, meta?.Pinned ?? false, false, path, kind));
        }
        return entries;
    }

    static IReadOnlyList<ApplicationHistoryEntry> ParseCustomDestinations(string path, CancellationToken token)
    {
        var data = File.ReadAllBytes(path);
        var result = new List<ApplicationHistoryEntry>();
        for (var offset = 0; offset <= data.Length - 76; offset++)
        {
            token.ThrowIfCancellationRequested();
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4)) != 0x4c) continue;
            var target = ShellLinkReader.TryReadTarget(data.AsSpan(offset).ToArray());
            if (!string.IsNullOrWhiteSpace(target) && !result.Any(x => x.TargetPath.Equals(target, StringComparison.OrdinalIgnoreCase)))
                result.Add(new(target, null, 0, false, false, path, "Custom"));
        }
        return result;
    }

    static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return path.Trim(); }
    }
}

public static class HistoricalPathRisk
{
    static readonly HashSet<string> SensitiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".env", ".pem", ".key", ".pfx", ".p12", ".kdbx", ".wallet", ".ovpn", ".rdp" };
    static readonly string[] SensitiveTokens =
        { "passport", "паспорт", "secret", "секрет", "credential", "парол", "contract", "договор", "client", "клиент", "finance", "финанс", "tax", "налог", "medical", "медицин" };

    public static int Score(string path)
    {
        var score = 0;
        if (SensitiveExtensions.Contains(Path.GetExtension(path))) score += 45;
        if (SensitiveTokens.Any(token => path.Contains(token, StringComparison.OrdinalIgnoreCase))) score += 30;
        if (path.StartsWith("\\\\", StringComparison.Ordinal)) score += 30;
        return Math.Min(score, 75);
    }
}

sealed record DestListMetadata(DateTime? LastInteraction, int InteractionCount, bool Pinned);

static class DestListReader
{
    public static Dictionary<long, DestListMetadata> Read(byte[] data)
    {
        var result = new Dictionary<long, DestListMetadata>();
        if (data.Length < 32) return result;
        var version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
        var offset = 32;
        while (offset + (version >= 3 ? 130 : 114) <= data.Length)
        {
            var fixedSize = version >= 3 ? 130 : 114;
            var idOffset = version >= 3 ? 88 : 88;
            var timeOffset = version >= 3 ? 100 : 100;
            var pinOffset = version >= 3 ? 108 : 108;
            var countOffset = version >= 3 ? 116 : 96;
            var nameLengthOffset = version >= 3 ? 128 : 112;
            var id = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset + idOffset, 8));
            var fileTime = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset + timeOffset, 8));
            var pin = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + pinOffset, 4));
            var count = countOffset + 4 <= fixedSize ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + countOffset, 4)) : 0;
            var chars = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + nameLengthOffset, 2));
            DateTime? time = null;
            try { if (fileTime > 0) time = DateTime.FromFileTime(fileTime); } catch { }
            result[id] = new(time, Math.Max(0, count), pin >= 0);
            var next = offset + fixedSize + chars * 2;
            if (next <= offset || next > data.Length) break;
            offset = next;
        }
        return result;
    }
}

public static class ShellLinkReader
{
    static readonly Encoding AnsiEncoding = CreateAnsiEncoding();
    static readonly byte[] LinkClsid = [0x01,0x14,0x02,0x00,0x00,0x00,0x00,0x00,0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46];

    public static string? TryReadTarget(byte[] data, int? ansiCodePage = null)
    {
        try
        {
            var ansi = ansiCodePage is int codePage ? Encoding.GetEncoding(codePage) : AnsiEncoding;
            if (data.Length < 76 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x4c || !data.AsSpan(4, 16).SequenceEqual(LinkClsid)) return null;
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20, 4));
            var unicode = (flags & 0x80) != 0;
            var offset = 76;
            if ((flags & 1) != 0)
            {
                if (offset + 2 > data.Length) return null;
                var idListSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
                offset += 2 + idListSize;
            }
            string? localBase = null, suffix = null;
            if ((flags & 2) != 0 && offset + 28 <= data.Length)
            {
                var infoStart = offset;
                var infoSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
                var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4));
                var localOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 16, 4));
                var suffixOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 24, 4));
                if (headerSize >= 36 && offset + 36 <= data.Length)
                {
                    var localUnicode = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 28, 4));
                    var suffixUnicode = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 32, 4));
                    localBase = ReadNullTerminated(data, infoStart + localUnicode, true, ansi) ?? ReadNullTerminated(data, infoStart + localOffset, false, ansi);
                    suffix = ReadNullTerminated(data, infoStart + suffixUnicode, true, ansi) ?? ReadNullTerminated(data, infoStart + suffixOffset, false, ansi);
                }
                else
                {
                    localBase = ReadNullTerminated(data, infoStart + localOffset, false, ansi);
                    suffix = ReadNullTerminated(data, infoStart + suffixOffset, false, ansi);
                }
                if (infoSize <= 0 || infoStart + infoSize > data.Length) return Clean(localBase, suffix);
                offset = infoStart + infoSize;
            }
            var strings = new List<string>();
            foreach (var bit in new uint[] { 4, 8, 16, 32, 64 })
            {
                if ((flags & bit) == 0) continue;
                if (offset + 2 > data.Length) break;
                var chars = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2)); offset += 2;
                var bytes = chars * (unicode ? 2 : 1);
                if (bytes < 0 || offset + bytes > data.Length) break;
                strings.Add(unicode ? Encoding.Unicode.GetString(data, offset, bytes) : DecodeAnsi(data, offset, bytes, ansi));
                offset += bytes;
            }
            var candidate = Clean(localBase, suffix);
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
            return strings.LastOrDefault(x => Path.IsPathRooted(x) || x.StartsWith("\\\\", StringComparison.Ordinal));
        }
        catch { return null; }
    }

    static string? Clean(string? root, string? suffix)
    {
        if (string.IsNullOrWhiteSpace(root)) return string.IsNullOrWhiteSpace(suffix) ? null : suffix.Trim('\0');
        root = root.Trim('\0'); suffix = suffix?.Trim('\0');
        if (string.IsNullOrWhiteSpace(suffix) || root.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return root;
        return Path.Combine(root, suffix);
    }

    static string? ReadNullTerminated(byte[] data, int offset, bool unicode, Encoding ansi)
    {
        if (offset <= 0 || offset >= data.Length) return null;
        if (unicode)
        {
            var end = offset;
            while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0)) end += 2;
            return end > offset ? Encoding.Unicode.GetString(data, offset, end - offset) : null;
        }
        var ansiEnd = Array.IndexOf(data, (byte)0, offset);
        if (ansiEnd < 0) ansiEnd = data.Length;
        if (ansiEnd <= offset) return null;
        return DecodeAnsi(data, offset, ansiEnd - offset, ansi);
    }

    static string DecodeAnsi(byte[] data, int offset, int count, Encoding ansi)
    {
        var bytes = data.AsSpan(offset, count).ToArray();
        var ansiText = ansi.GetString(bytes);
        try
        {
            var utf8 = new UTF8Encoding(false, true).GetString(bytes);
            var utf8HasCyrillic = utf8.Any(c => c is >= '\u0400' and <= '\u04FF');
            var ansiLooksMojibake = ansiText.Contains('Р') || ansiText.Contains('С') || ansiText.Contains('�');
            if (utf8HasCyrillic && ansiLooksMojibake) return utf8;
        }
        catch (DecoderFallbackException) { }
        return ansiText;
    }

    static Encoding CreateAnsiEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage); }
        catch { return Encoding.Latin1; }
    }
}

sealed class CompoundFile
{
    public sealed record CompoundStream(string Name, byte[] Data);
    public IReadOnlyList<CompoundStream> Streams { get; private init; } = [];

    public static CompoundFile Read(Stream input)
    {
        using var memory = new MemoryStream(); input.CopyTo(memory); var data = memory.ToArray();
        if (data.Length < 512 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1 })) throw new InvalidDataException("Not an OLE compound file.");
        var sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(30, 2));
        var miniShift = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(32, 2));
        var sectorSize = 1 << sectorShift; var miniSize = 1 << miniShift;
        if (sectorSize is not (512 or 4096) || miniSize != 64) throw new InvalidDataException("Unsupported compound file geometry.");
        var fat = ReadFat(data, sectorSize);
        var directorySector = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(48, 4));
        var directory = ReadChain(data, sectorSize, fat, directorySector, data.Length);
        var entries = new List<(string Name, byte Type, int Start, long Size)>();
        for (var offset = 0; offset + 128 <= directory.Length; offset += 128)
        {
            var nameBytes = BinaryPrimitives.ReadUInt16LittleEndian(directory.AsSpan(offset + 64, 2));
            if (nameBytes < 2 || nameBytes > 64) continue;
            var name = Encoding.Unicode.GetString(directory, offset, nameBytes - 2);
            var type = directory[offset + 66];
            var start = BinaryPrimitives.ReadInt32LittleEndian(directory.AsSpan(offset + 116, 4));
            var size = BinaryPrimitives.ReadInt64LittleEndian(directory.AsSpan(offset + 120, 8));
            entries.Add((name, type, start, size));
        }
        var root = entries.FirstOrDefault(x => x.Type == 5);
        var cutoff = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(56, 4));
        var miniFatStart = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(60, 4));
        var miniFatBytes = miniFatStart >= 0 ? ReadChain(data, sectorSize, fat, miniFatStart, data.Length) : [];
        var miniFat = new int[miniFatBytes.Length / 4];
        for (var i = 0; i < miniFat.Length; i++) miniFat[i] = BinaryPrimitives.ReadInt32LittleEndian(miniFatBytes.AsSpan(i * 4, 4));
        var miniStream = root.Size > 0 ? ReadChain(data, sectorSize, fat, root.Start, (int)Math.Min(root.Size, int.MaxValue)) : [];
        var streams = new List<CompoundStream>();
        foreach (var entry in entries.Where(x => x.Type == 2 && x.Size >= 0 && x.Size <= int.MaxValue))
        {
            byte[] bytes;
            if (entry.Size < cutoff && entry.Start >= 0) bytes = ReadMiniChain(miniStream, miniSize, miniFat, entry.Start, (int)entry.Size);
            else bytes = ReadChain(data, sectorSize, fat, entry.Start, (int)entry.Size);
            streams.Add(new(entry.Name, bytes));
        }
        return new() { Streams = streams };
    }

    static int[] ReadFat(byte[] data, int sectorSize)
    {
        var sectors = new List<int>();
        for (var i = 0; i < 109; i++) { var s = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(76 + i * 4, 4)); if (s >= 0) sectors.Add(s); }
        var fat = new List<int>();
        foreach (var sector in sectors)
        {
            var offset = SectorOffset(sector, sectorSize); if (offset < 0 || offset + sectorSize > data.Length) continue;
            for (var i = 0; i < sectorSize; i += 4) fat.Add(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + i, 4)));
        }
        return fat.ToArray();
    }

    static byte[] ReadChain(byte[] data, int sectorSize, int[] fat, int start, int wanted)
    {
        using var output = new MemoryStream(); var sector = start; var seen = new HashSet<int>();
        while (sector >= 0 && sector < fat.Length && seen.Add(sector) && output.Length < wanted)
        {
            var offset = SectorOffset(sector, sectorSize); if (offset < 0 || offset + sectorSize > data.Length) break;
            output.Write(data, offset, Math.Min(sectorSize, wanted - (int)Math.Min(output.Length, int.MaxValue)));
            sector = fat[sector];
        }
        return output.ToArray();
    }

    static byte[] ReadMiniChain(byte[] mini, int miniSize, int[] fat, int start, int wanted)
    {
        using var output = new MemoryStream(); var sector = start; var seen = new HashSet<int>();
        while (sector >= 0 && sector < fat.Length && seen.Add(sector) && output.Length < wanted)
        {
            var offset = sector * miniSize; if (offset < 0 || offset >= mini.Length) break;
            output.Write(mini, offset, Math.Min(Math.Min(miniSize, mini.Length - offset), wanted - (int)output.Length));
            sector = fat[sector];
        }
        return output.ToArray();
    }

    static int SectorOffset(int sector, int sectorSize) => checked((sector + 1) * sectorSize);
}
